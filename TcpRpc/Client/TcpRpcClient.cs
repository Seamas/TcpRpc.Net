using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;
using TcpRpc.Message;

namespace TcpRpc.Client;

public class TcpRpcClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<TcpRpcClient> _logger;

    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests = new();
    private long _nextMessageId = 1;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _pendingPings = new();
    
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Task? _receiveTask;
    
    // 状态管理
    private volatile RpcClientState _state = RpcClientState.Created;
    private readonly Lock _stateLock = new ();
    private int _disposed; // 使用int作为轻量级标志，0=未释放，1=已释放

    public RpcClientState State => _state;

    public TcpRpcClient(string host, int port, IMessageSerializer serializer, ILoggerFactory factory)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _serializer = serializer;
        _logger = factory.CreateLogger<TcpRpcClient>();
    }

    /// <summary>
    /// 连接
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task ConnectAsync()
    {
        CheckDisposed();

        lock (_stateLock)
        {
            if (_state != RpcClientState.Created && _state != RpcClientState.Disconnected)
                throw new InvalidOperationException($"Cannot connect from current state: {_state}");

            _state = RpcClientState.Connecting;
        }

        try
        {
            // 清理旧资源
            _stream?.Dispose();
            _tcpClient?.Dispose();
            
            // 如果CTS已被取消，重新创建
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
            }

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_host, _port);
            _stream = _tcpClient.GetStream();

            _logger.LogInformation("Connected to server {Host}:{Port}", _host, _port);

            // 先启动接收任务，再设置状态，避免竞态条件
            // ReceiveAsync 现在只检查 _stream != null，不依赖 _state
            _receiveTask = ReceiveAsync(_cts.Token);
            
            lock (_stateLock)
            {
                _state = RpcClientState.Connected;
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _state = RpcClientState.Disconnected;
            }
            _logger.LogError(ex, "Failed to connect to server {Host}:{Port}", _host, _port);
            throw;
        }
    }

    /// <summary>
    /// 断开
    /// </summary>
    public async Task DisconnectAsync()
    {
        CheckDisposed();

        lock (_stateLock)
        {
            if (_state != RpcClientState.Connected)
                return;

            _state = RpcClientState.Disconnecting;
        }

        try
        {
            await _cts.CancelAsync();
            
            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    // 忽略超时
                }
            }

            _stream?.Close();
            _tcpClient?.Close();
        }
        finally
        {
            // 清理所有挂起的请求
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new Exception("Client disconnected"));
            }
            _pendingRequests.Clear();

            lock (_stateLock)
            {
                _state = RpcClientState.Disconnected;
            }
        }
    }

    public async Task<ResponseMessage> SendRequestAsync(string method, byte[] data, int timeoutMs = 30000)
    {
        CheckDisposed();

        if (_state != RpcClientState.Connected)
            throw new InvalidOperationException($"Client is not connected. Current state: {_state}");

        var messageId = Interlocked.Increment(ref _nextMessageId);
        _logger.LogDebug("Sending request {MessageId} for method {Method}", messageId, method);
        
        var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        if (!_pendingRequests.TryAdd(messageId, tcs))
            throw new InvalidOperationException($"Failed to add pending request {messageId}");
        
        var header = new MessageHeader
        {
            Type = MessageType.Request,
            MessageId = messageId,
            ContentLength = data.Length
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                var headerBuffer = header.Serialize();
                await _stream!.WriteAsync(headerBuffer,  linkedCts.Token);
                await _stream!.WriteAsync(data,  linkedCts.Token);
                await _stream!.FlushAsync(linkedCts.Token);
                _logger.LogDebug("Request {MessageId} sent, waiting for response...", messageId);
            }
            finally
            {
                _sendLock.Release();
            }

            var response = await tcs.Task.WaitAsync(linkedCts.Token);
            _logger.LogDebug("Request {MessageId} received response: Success={Success}", messageId, response.Success);
            return response;
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            _pendingRequests.TryRemove(messageId, out _);
            _logger.LogWarning("Request {MessageId} timed out after {TimeoutMs}ms", messageId, timeoutMs);
            throw new TimeoutException($"Request {messageId} timed out after {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(messageId, out _);
            _logger.LogError(ex, "Request {MessageId} failed", messageId);
            throw;
        }
    }
    
    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Receive loop started");
            // 只依赖 cancellationToken 和 _stream，不依赖 _state
            // 避免 ConnectAsync 中的竞态条件
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                _logger.LogDebug("Waiting to read message header...");
                var headerBuffer = await ReadBytesAsync(MessageHeader.HeaderSize, cancellationToken);
                
                var header = MessageHeader.Deserialize(headerBuffer);
                _logger.LogDebug("Received header: Type={Type}, MessageId={MessageId}, ContentLength={ContentLength}", 
                    header.Type, header.MessageId, header.ContentLength);
                
                if (header.ContentLength > 10 * 1024 * 1024) // 10MB limit
                {
                    throw new InvalidDataException($"Message too large: {header.ContentLength} bytes");
                }

                var contentBuffer = await ReadBytesAsync(header.ContentLength, cancellationToken);
                _logger.LogDebug("Received content: {Length} bytes", contentBuffer.Length);
                
                await ProcessMessageAsync(header, contentBuffer);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError("OperationCanceledException {Message}", ex.Message);
            _logger.LogDebug("Receive loop cancelled");
            
            // Normal shutdown
        }
        catch (Exception ex)
        {
            // _logger.LogError(ex, "Receive loop error");
            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleConnectionLostAsync($"Connection error: {ex.Message}");
            }
        }
        _logger.LogDebug("Receive loop ended");
    }

    private async Task<byte[]> ReadBytesAsync(int bufferSize, CancellationToken cancellationToken)
    {
        var bytesRead = 0;
        var buffer = new byte[bufferSize];
        while (bytesRead < bufferSize)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(bytesRead, 
                bufferSize - bytesRead), cancellationToken);
            if (read == 0)
            {
                // Connection closed by server
                await HandleConnectionLostAsync("Server closed connection");
                throw new OperationCanceledException("can't read message");
            }
            bytesRead += read;
        }

        return buffer;
    }

    private Task ProcessMessageAsync(MessageHeader header, byte[] content)
    {
        var headerMessageId = header.MessageId;
        _logger.LogDebug("Processing message: Type={Type}, MessageId={MessageId}", header.Type, headerMessageId);
        switch (header.Type)
        {
            case MessageType.Response:
                var response = _serializer.DeserializeResponse(content);
                if (_pendingRequests.TryRemove(headerMessageId, out var tcs))
                {
                    _logger.LogDebug("Setting result for request {MessageId}", headerMessageId);
                    tcs.TrySetResult(response);
                }
                else
                {
                    _logger.LogWarning("Received response for unknown request {MessageId}", headerMessageId);
                }
                break;

            case MessageType.Error:
                var errorResponse = _serializer.DeserializeResponse(content);
                if (_pendingRequests.TryRemove(headerMessageId, out var errorTcs))
                {
                    _logger.LogDebug("Setting error for request {MessageId}: {Error}", headerMessageId, errorResponse.Error);
                    errorTcs.TrySetException(new RpcException(
                        errorResponse.Error ?? "Unknown server error",
                        errorResponse.ExceptionType,
                        errorResponse.StackTrace));
                }
                else
                {
                    _logger.LogWarning("Received error for unknown request {MessageId}: {Error}", headerMessageId, errorResponse.Error);
                }
                break;

            case MessageType.Pong:
                if (_pendingPings.TryRemove(headerMessageId, out var pingTcs))
                {
                    _logger.LogDebug("Received pong for ping {MessageId}", headerMessageId);
                    pingTcs.TrySetResult(true);
                }
                else
                {
                    _logger.LogWarning("Received pong for unknown ping {MessageId}", headerMessageId);
                }
                break;
        }

        return Task.CompletedTask;
    }

    private async Task HandleConnectionLostAsync(string reason)
    {
        lock (_stateLock)
        {
            if (_state == RpcClientState.Connected)
            {
                _state = RpcClientState.Disconnected;
            }
        }

        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(new Exception($"Connection lost: {reason}"));
        }
        _pendingRequests.Clear();

        await Task.CompletedTask;
    }
    
    
    private bool IsConnected()
    {
        return _tcpClient?.Connected == true && _stream != null && _state == RpcClientState.Connected;
    }

    private void CheckDisposed()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) == 1)
            throw new ObjectDisposedException(nameof(TcpRpcClient));
    }

    /// <summary>
    /// 心跳检测
    /// </summary>
    /// <param name="timeoutMs"></param>
    /// <returns></returns>
    public async Task<bool> PingAsync(int timeoutMs = 5000)
    {
        CheckDisposed();

        if (_state != RpcClientState.Connected)
            return false;

        var messageId = Interlocked.Increment(ref _nextMessageId);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        if (!_pendingPings.TryAdd(messageId, tcs))
        {
            _logger.LogWarning("Failed to add pending ping {MessageId}", messageId);
            return false;
        }

        try
        {
            var header = new MessageHeader
            {
                Type = MessageType.Ping,
                MessageId = messageId,
                ContentLength = 0
            };

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                var headerBuffer = header.Serialize();
                await _stream!.WriteAsync(headerBuffer, 0, headerBuffer.Length, linkedCts.Token);
                await _stream!.FlushAsync(linkedCts.Token);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait for pong with timeout
            await tcs.Task.WaitAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            _logger.LogWarning("Ping {MessageId} timed out after {TimeoutMs}ms", messageId, timeoutMs);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping {MessageId} failed", messageId);
            return false;
        }
        finally
        {
            _pendingPings.TryRemove(messageId, out _);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 忽略断开连接时的错误
        }

        _sendLock.Dispose();
        _cts.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();

        GC.SuppressFinalize(this);
    }
}