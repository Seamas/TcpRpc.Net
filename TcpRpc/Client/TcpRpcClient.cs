using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;
using TcpRpc.Message;
using TcpRpc.Network;

namespace TcpRpc.Client;

public class TcpRpcClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<TcpRpcClient> _logger;
    private readonly ClientAuthOptions _authOptions;

    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<AuthResponseMessage>> _pendingAuthRequests = new();
    private long _nextMessageId = 1;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _pendingPings = new();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Task? _receiveTask;

    // 状态管理
    private volatile RpcClientState _state = RpcClientState.Created;
    private readonly Lock _stateLock = new();
    private int _disposed; // 使用int作为轻量级标志，0=未释放，1=已释放

    // 认证状态
    private string? _clientId;
    private string? _authToken;
    private DateTime? _tokenExpiresAt;
    private int _reconnectAttemptCount;

    public RpcClientState State => _state;

    /// <summary>
    /// 是否已认证
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);

    /// <summary>
    /// 当前认证 Token
    /// </summary>
    public string? AuthToken => _authToken;

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId => _clientId;

    /// <summary>
    /// Token 过期时间
    /// </summary>
    public DateTime? TokenExpiresAt => _tokenExpiresAt;

    public TcpRpcClient(string host, int port, IMessageSerializer serializer, ILoggerFactory factory, ClientAuthOptions? authOptions = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _serializer = serializer;
        _logger = factory.CreateLogger<TcpRpcClient>();
        _authOptions = authOptions ?? new ClientAuthOptions();
    }

    /// <summary>
    /// 连接
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed())
        {
            throw new InvalidOperationException($"Client is disposed.");
        }

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
                _cts = new CancellationTokenSource();
            }

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_host, _port).WaitAsync(_authOptions.ConnectTimeout, cancellationToken);
            _stream = _tcpClient.GetStream();

            _logger.LogInformation("Connected to server {Host}:{Port}", _host, _port);

            // 先启动接收任务，再设置状态，避免竞态条件
            _receiveTask = ReceiveAsync(_cts.Token);

            lock (_stateLock)
            {
                _state = RpcClientState.Connected;
            }

            // 重置重连计数
            _reconnectAttemptCount = 0;
            _authOptions.ReconnectPolicy.Reset();
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
    /// 连接并认证
    /// </summary>
    public async Task ConnectAndAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken);
        await AuthenticateAsync(cancellationToken);
    }

    /// <summary>
    /// 执行认证
    /// </summary>
    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed())
        {
            throw new InvalidOperationException($"Client is disposed.");
        }

        if (_state != RpcClientState.Connected)
            throw new InvalidOperationException($"Client is not connected. Current state: {_state}");

        var isReconnect = !string.IsNullOrEmpty(_authToken);

        var authRequest = new AuthRequestMessage
        {
            ClientId = _clientId ?? _authOptions.ClientId,
            PreviousToken = isReconnect ? _authToken : null,
            Credentials = _authOptions.Credentials
        };

        var messageId = Interlocked.Increment(ref _nextMessageId);
        var tcs = new TaskCompletionSource<AuthResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingAuthRequests.TryAdd(messageId, tcs))
            throw new InvalidOperationException($"Failed to add pending auth request {messageId}");

        try
        {
            var requestData = _serializer.SerializeAuthRequest(authRequest);
            var header = new MessageHeader
            {
                Type = MessageType.AuthRequest,
                MessageId = messageId,
                ContentLength = requestData.Length
            };

            using var timeoutCts = new CancellationTokenSource(_authOptions.AuthTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token, cancellationToken);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                await NetworkService.WriteMessageAsync(_stream!, header, requestData, linkedCts.Token);
                
                _logger.LogDebug("Auth request {MessageId} sent", messageId);
            }
            finally
            {
                _sendLock.Release();
            }

            var response = await tcs.Task.WaitAsync(linkedCts.Token);

            if (!response.Success)
            {
                throw new RpcException($"Authentication failed: {response.ErrorMessage}");
            }

            // 保存认证信息
            _clientId = response.ClientId;
            _authToken = response.Token;
            _tokenExpiresAt = response.ExpiresAt;

            if (response.IsSessionRestored)
            {
                _logger.LogInformation("Session restored successfully for client {ClientId}", _clientId);
            }
            else
            {
                _logger.LogInformation("Authenticated successfully as {ClientId}", _clientId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _pendingAuthRequests.TryRemove(messageId, out _);
            _logger.LogWarning("Authentication timed out");
            throw new TimeoutException("Authentication timed out");
        }
        catch (Exception ex)
        {
            _pendingAuthRequests.TryRemove(messageId, out _);
            _logger.LogError(ex, "Authentication failed");
            throw;
        }
    }

    /// <summary>
    /// 尝试自动重连
    /// </summary>
    public async Task<bool> TryReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisposed())
        {
            return false;
        }
        
        if (!_authOptions.AutoReconnect)
            return false;

        _reconnectAttemptCount++;

        if (!_authOptions.ReconnectPolicy.CanReconnect(_reconnectAttemptCount))
        {
            _logger.LogWarning("Max reconnect attempts ({Max}) reached", _authOptions.ReconnectPolicy.MaxReconnectAttempts);
            return false;
        }

        var delay = _authOptions.ReconnectPolicy.GetDelay(_reconnectAttemptCount);
        _logger.LogInformation("Attempting reconnect {Attempt}/{Max} after {DelayMs}ms",
            _reconnectAttemptCount, _authOptions.ReconnectPolicy.MaxReconnectAttempts, delay.TotalMilliseconds);

        try
        {
            await Task.Delay(delay, cancellationToken);
            await ConnectAndAuthenticateAsync(cancellationToken);


            _logger.LogInformation("Reconnected successfully");
            _reconnectAttemptCount = 0; // 重置计数
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnect attempt {Attempt} failed", _reconnectAttemptCount);
            return false;
        }
    }

    /// <summary>
    /// 断开
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (IsDisposed())
        {
            return;
        }

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
        if (IsDisposed())
        {
            throw new Exception("Client is disposed");
        }

        return await SendRequestWithRetryAsync(method, data, timeoutMs);
    }

    private async Task<ResponseMessage> SendRequestWithRetryAsync(string method, byte[] requestData, int timeoutMs, int retryCount = 0)
    {
        if (_state != RpcClientState.Connected)
        {
            // 尝试自动重连
            if (_authOptions.AutoReconnect && await TryReconnectAsync())
            {
                return await SendRequestWithRetryAsync(method, requestData, timeoutMs, retryCount + 1);
            }
            throw new InvalidOperationException($"Client is not connected. Current state: {_state}");
        }

        var messageId = Interlocked.Increment(ref _nextMessageId);
        _logger.LogDebug("Sending request {MessageId} for method {Method}", messageId, method);

        var tcs = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(messageId, tcs))
            throw new InvalidOperationException($"Failed to add pending request {messageId}");

        // 使用带 Token 的消息头
        var header = new MessageHeader
        {
            Type = MessageType.Request,
            MessageId = messageId,
            ContentLength = requestData.Length,
            Token = _authToken
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                await NetworkService.WriteMessageAsync(_stream!, header, requestData, linkedCts.Token);
                
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
        catch (Exception ex) when (IsConnectionError(ex) && _authOptions.AutoReconnect && retryCount == 0)
        {
            _pendingRequests.TryRemove(messageId, out _);
            _logger.LogWarning("Request {MessageId} failed due to connection error, attempting reconnect...", messageId);

            if (await TryReconnectAsync())
            {
                return await SendRequestWithRetryAsync(method, requestData, timeoutMs, retryCount + 1);
            }
            throw;
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(messageId, out _);
            _logger.LogError(ex, "Request {MessageId} failed", messageId);
            throw;
        }
    }

    private static bool IsConnectionError(Exception ex)
    {
        return ex is IOException || ex is SocketException || ex is InvalidOperationException;
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
                var header = await NetworkService.ReadMessageHeaderAsync(_stream, cancellationToken);
                if (header == null)
                {
                    _logger.LogDebug("Connection closed while reading header");
                    return;
                }

                // 防御性检查
                if (header.ContentLength < 0)
                {
                    throw new InvalidDataException($"Invalid ContentLength: {header.ContentLength}");
                }

                _logger.LogDebug("Received header: Type={Type}, MessageId={MessageId}, ContentLength={ContentLength}",
                    header.Type, header.MessageId, header.ContentLength);

                byte[]? readBytesAsync;
                if (header.ContentLength == 0)
                {
                    readBytesAsync = Array.Empty<byte>();
                }
                else
                {
                    readBytesAsync = await NetworkService.ReadBytesAsync(_stream, header.ContentLength, cancellationToken);
                    if (readBytesAsync == null)
                    {
                        _logger.LogDebug("Connection closed while reading content");
                        return;
                    }
                }

                _logger.LogDebug("Received content: {Length} bytes", readBytesAsync.Length);
                await ProcessMessageAsync(header, readBytesAsync);
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

            case MessageType.AuthResponse:
                var authResponse = _serializer.DeserializeAuthResponse(content);
                if (_pendingAuthRequests.TryRemove(headerMessageId, out var authTcs))
                {
                    _logger.LogDebug("Setting auth result for request {MessageId}", headerMessageId);
                    authTcs.TrySetResult(authResponse);
                }
                else
                {
                    _logger.LogWarning("Received auth response for unknown request {MessageId}", headerMessageId);
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

        foreach (var kvp in _pendingAuthRequests)
        {
            kvp.Value.TrySetException(new Exception($"Connection lost: {reason}"));
        }
        _pendingAuthRequests.Clear();

        // 触发自动重连
        if (_authOptions.AutoReconnect && _authOptions.ReconnectPolicy.CanReconnect(_reconnectAttemptCount + 1))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await TryReconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background reconnect failed");
                }
            });
        }

        await Task.CompletedTask;
    }
    
    
    private bool IsConnected()
    {
        return _tcpClient?.Connected == true && _stream != null && _state == RpcClientState.Connected;
    }

    private bool IsDisposed()
    {
        return Interlocked.CompareExchange(ref _disposed, 0, 0) == 1;
    }

    /// <summary>
    /// 心跳检测
    /// </summary>
    /// <param name="timeoutMs"></param>
    /// <returns></returns>
    public async Task<bool> PingAsync(int timeoutMs = 5000)
    {
        if (IsDisposed())
        {
            return false;
        }

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
                await NetworkService.WriteMessageAsync(_stream!, header, null, linkedCts.Token);
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
        {
            return;
        }

        try
        {
            _logger.LogInformation("Disposing client");
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