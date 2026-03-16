using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using TcpRpc.Formatter;
using TcpRpc.Message;

namespace TcpRpc.Client;

public class RpcClient(string host, int port, IMessageSerializer? serializer = null)
    : IDisposable
{
    private readonly string _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly IMessageSerializer _serializer = serializer ?? new JsonMessageSerializer();

    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests = new();
    private long _nextMessageId;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    
    
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Task? _receiveTask;
    
    // 状态管理
    private volatile RpcClientState _state = RpcClientState.Created;
    private readonly Lock _stateLock = new ();
    private int _disposed; // 使用int作为轻量级标志，0=未释放，1=已释放

    public RpcClientState State => _state;

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
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_host, port);
            _stream = _tcpClient.GetStream();

            _receiveTask = ReceiveAsync(_cts.Token);

            lock (_stateLock)
            {
                _state = RpcClientState.Connected;
            }
        }
        catch (Exception)
        {
            lock (_stateLock)
            {
                _state = RpcClientState.Disconnected;
            }
            throw;
        }
    }

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
            _cts.Cancel();
            
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
        var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        
        if (!_pendingRequests.TryAdd(messageId, tcs))
            throw new InvalidOperationException($"Failed to add pending request {messageId}");

        var request = new RequestMessage
        {
            MessageId = messageId,
            Method = method,
            Data = data
        };

        var requestData = _serializer.SerializeRequest(request);
        var header = new MessageHeader
        {
            Type = MessageType.Request,
            MessageId = messageId,
            ContentLength = requestData.Length
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                var headerBuffer = header.Serialize();
                await _stream!.WriteAsync(headerBuffer, 0, headerBuffer.Length, linkedCts.Token);
                await _stream!.WriteAsync(requestData, 0, requestData.Length, linkedCts.Token);
            }
            finally
            {
                _sendLock.Release();
            }

            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            _pendingRequests.TryRemove(messageId, out _);
            throw new TimeoutException($"Request {messageId} timed out after {timeoutMs}ms");
        }
        catch (Exception)
        {
            _pendingRequests.TryRemove(messageId, out _);
            throw;
        }
    }

    public async Task<T> SendRequestAsync<T>(string method, object data)
    {
        var dataBytes = JsonSerializer.SerializeToUtf8Bytes(data);
        
        var response = await SendRequestAsync(method, dataBytes);

        if (!response.Success)
            throw new Exception($"Request failed: {response.Error}");

        var result = JsonSerializer.Deserialize<T>(response.Data);
        
        return result ?? throw new NullReferenceException($"Response data is null");
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected())
            {
                var headerBuffer = await ReadBytesAsync(MessageHeader.HeaderSize, cancellationToken);
                
                var header = MessageHeader.Deserialize(headerBuffer);
                
                if (header.ContentLength > 10 * 1024 * 1024) // 10MB limit
                {
                    throw new InvalidDataException($"Message too large: {header.ContentLength} bytes");
                }

                var contentBuffer = await ReadBytesAsync(header.ContentLength, cancellationToken);
                
                await ProcessMessageAsync(header, contentBuffer);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleConnectionLostAsync($"Connection error: {ex.Message}");
            }
        }
    }

    private async Task<byte[]> ReadBytesAsync(long bufferSize, CancellationToken cancellationToken)
    {
        var bytesRead = 0;
        var buffer = new byte[bufferSize];
        while (bytesRead < MessageHeader.HeaderSize)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(bytesRead, 
                MessageHeader.HeaderSize - bytesRead), cancellationToken);
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

    private async Task ProcessMessageAsync(MessageHeader header, byte[] content)
    {
        switch (header.Type)
        {
            case MessageType.Response:
                var response = _serializer.DeserializeResponse(content);
                if (_pendingRequests.TryRemove(response.MessageId, out var tcs))
                {
                    tcs.TrySetResult(response);
                }
                break;

            case MessageType.Error:
                var errorMsg = System.Text.Encoding.UTF8.GetString(content);
                if (_pendingRequests.TryRemove(header.MessageId, out var errorTcs))
                {
                    errorTcs.TrySetException(new Exception($"Server error: {errorMsg}"));
                }
                break;
        }
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
            throw new ObjectDisposedException(nameof(RpcClient));
    }

    public async Task<bool> PingAsync(int timeoutMs = 5000)
    {
        CheckDisposed();

        if (_state != RpcClientState.Connected)
            return false;

        try
        {
            var messageId = Interlocked.Increment(ref _nextMessageId);

            var header = new MessageHeader
            {
                Type = MessageType.Ping,
                MessageId = messageId,
                ContentLength = 0
            };

            await _sendLock.WaitAsync();
            try
            {
                var headerBuffer = header.Serialize();
                await _stream!.WriteAsync(headerBuffer, 0, headerBuffer.Length);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait for pong
            using var cts = new CancellationTokenSource(timeoutMs);
            // TODO: 实际应该有对应的pong处理
            return true;
        }
        catch
        {
            return false;
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