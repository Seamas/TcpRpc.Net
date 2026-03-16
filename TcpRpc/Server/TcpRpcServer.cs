using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;
using TcpRpc.Message;
using TcpRpc.Network;
using TcpRpc.Server.Auth;

namespace TcpRpc.Server;

/// <summary>
/// TCP RPC服务器 - 支持服务注册和拦截器
/// </summary>
public class TcpRpcServer : IDisposable
{
    private readonly int _port;
    private readonly IMessageSerializer _serializer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TcpRpcServer> _logger;
    private CancellationTokenSource _cts = new();
    private readonly List<Task> _clientTasks = [];
    private readonly ConcurrentDictionary<string, RequestHandler> _handlers = new();

    private TcpListener? _listener;

    // 状态管理
    private volatile RpcServerState _state = RpcServerState.Stopped;
    private readonly Lock _stateLock = new();
    private int _disposed;
    private int _clientCounter;

    // 认证相关
    private readonly ServerAuthOptions _authOptions;
    private ClientSessionManager? _sessionManager;

    /// <summary>
    /// 服务注册表
    /// </summary>
    public ServiceRegistry ServiceRegistry { get; }

    /// <summary>
    /// 会话管理器
    /// </summary>
    public ClientSessionManager SessionManager => _sessionManager ?? throw new InvalidOperationException("Server not started");

    public RpcServerState State => _state;

    public int ActiveConnections
    {
        get
        {
            lock (_clientTasks)
            {
                _clientTasks.RemoveAll(t => t.IsCompleted);
                return _clientTasks.Count;
            }
        }
    }

    /// <summary>
    /// 是否启用认证
    /// </summary>
    public bool IsAuthEnabled => _authOptions.RequireAuth;

    public TcpRpcServer(int port, IMessageSerializer serializer, ILoggerFactory factory, ServerAuthOptions authOptions)
    {
        _port = port;
        _serializer = serializer;
        _loggerFactory = factory;
        _logger = factory.CreateLogger<TcpRpcServer>();
        ServiceRegistry = new ServiceRegistry(factory);
        _authOptions = authOptions;
    }

    
    /// <summary>
    /// 注册服务实现（新API）
    /// </summary>
    public void RegisterService<TInterface, TImplementation>(TImplementation instance) where TImplementation : class, TInterface
    {
        ServiceRegistry.Register<TInterface, TImplementation>(instance);
    }

    public async Task StartAsync()
    {
        CheckDisposed();

        lock (_stateLock)
        {
            if (_state != RpcServerState.Stopped)
                throw new InvalidOperationException($"Server is already {_state}");

            _state = RpcServerState.Starting;
        }

        try
        {
            // 初始化认证相关组件
            if (_authOptions.RequireAuth)
            {
                _sessionManager = new ClientSessionManager(
                    _loggerFactory,
                    _authOptions.SessionTimeout,
                    _authOptions.SessionCleanupInterval);

                // 使用默认的匿名认证提供者（如果没有指定）
                _authOptions.TokenGenerator ??= new SimpleTokenGenerator();

                _logger.LogInformation("Authentication enabled with {Provider}", _authOptions.AuthProvider.GetType().Name);
            }

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            // 如果CTS已被取消，重新创建
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }

            var cancellationToken = _cts.Token;

            lock (_stateLock)
            {
                _state = RpcServerState.Running;
            }

            _logger.LogInformation("Server started on port {Port}", _port);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var clientId = Interlocked.Increment(ref _clientCounter);
                    var clientTask = HandleClientAsync(client, clientId, cancellationToken);
                    
                    lock (_clientTasks)
                    {
                        _clientTasks.Add(clientTask);
                        // 清理已完成的任务
                        _clientTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }
        catch (Exception)
        {
            lock (_stateLock)
            {
                _state = RpcServerState.Faulted;
            }
            throw;
        }
        finally
        {
            if (_state == RpcServerState.Running)
            {
                lock (_stateLock)
                {
                    _state = RpcServerState.Stopped;
                }
            }
        }
    }

    public async Task StopAsync()
    {
        CheckDisposed();

        lock (_stateLock)
        {
            if (_state != RpcServerState.Running)
                return;

            _state = RpcServerState.Stopping;
        }

        try
        {
            // 停止接受新连接
            _listener?.Stop();

            // 取消所有操作
            _cts.Cancel();

            // 等待所有客户端连接关闭
            Task[] tasks;
            lock (_clientTasks)
            {
                tasks = _clientTasks.ToArray();
            }

            if (tasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timeout waiting for {Count} clients to disconnect", tasks.Length);
                }
            }

            lock (_clientTasks)
            {
                _clientTasks.Clear();
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _state = RpcServerState.Stopped;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, int clientId, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Client {ClientId} connected: {Endpoint}", clientId, endpoint);

        // 客户端连接上下文
        var context = new ClientConnectionContext
        {
            IsAuthenticated = !_authOptions.RequireAuth
        };

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        // 读取扩展消息头（支持认证 Token）
                        var header = await NetworkService.ReadMessageHeaderAsync(stream, cancellationToken);
                        if (header == null)
                        {
                            _logger.LogInformation("Client {ClientId} disconnected", clientId);
                            return;
                        }

                        // 防御性检查
                        if (header.ContentLength < 0)
                        {
                            _logger.LogError("Invalid ContentLength: {ContentLength}", header.ContentLength);
                            return;
                        }

                        byte[]? contentBuffer;
                        if (header.ContentLength == 0)
                        {
                            contentBuffer = Array.Empty<byte>();
                        }
                        else
                        {
                            contentBuffer = await ReadBytesAsync(stream, header.ContentLength, cancellationToken);
                            // 读取内容为空表示连接关闭
                            if (contentBuffer == null)
                            {
                                return;
                            }
                            // 读取内容长度为0 表示内容太大, 丢弃
                            if (contentBuffer.Length == 0)
                            {
                                await SendAuthErrorResponseAsync(stream, header.MessageId,
                                    $"消息体太长, 长度: {header.ContentLength}", cancellationToken);
                                continue;
                            }
                        }
                        
                        // 处理消息
                        await ProcessClientMessageAsync(stream, header, contentBuffer,
                            context, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Server shutting down
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout, continue waiting
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing client {ClientId} message", clientId);
                        // Continue to next message
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {ClientId}", clientId);
        }
        finally
        {
            _logger.LogInformation("Client {ClientId} disconnected: {Endpoint}", clientId, endpoint);
        }
    }
    
    
    /// <summary>
    /// 根据长度读取内容
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="contentLength"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>返回读取的内容，如果连接关闭则返回null, 如果返回为[] 表示内容太长</returns>
    private async Task<byte[]?> ReadBytesAsync(NetworkStream stream, int contentLength, CancellationToken cancellationToken)
    {
        if (contentLength < 0)
        {
            throw new InvalidDataException($"Content length cannot be negative: {contentLength}");
        }

        if (contentLength == 0)
        {
            return Array.Empty<byte>();
        }

        if (contentLength >= 100 * 1024)
        {
            // 读取剩余内容并丢弃, 以免下个消息出错
            const int bufferSize = 8196;
            var buffer = new byte[bufferSize];
            var bytesRead = 0;
            while (bytesRead < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken);
                if (read == 0)
                {
                    return null; // 连接关闭
                }
                bytesRead += read;
            }
            
            return [];
        }
        else
        {
            var buffer = new byte[contentLength];
            var bytesRead = 0;
            while (bytesRead < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, contentLength - bytesRead), cancellationToken);
                if (read == 0)
                {
                    return null; // 连接关闭
                }
                bytesRead += read;
            }
            return buffer;
        }
    }
    

    /// <summary>
    /// 客户端连接上下文 - 用于在异步方法间传递状态
    /// </summary>
    private class ClientConnectionContext
    {
        public ClientSession? Session { get; set; }
        public bool IsAuthenticated { get; set; }
    }

    private async Task ProcessClientMessageAsync(NetworkStream stream, MessageHeader header,
        byte[] content, ClientConnectionContext context,
        CancellationToken cancellationToken)
    {
        switch (header.Type)
        {
            case MessageType.AuthRequest:
                var authRequest = _serializer.DeserializeAuthRequest(content);
                var authResponse = await ProcessAuthRequestAsync(authRequest, context);
                await SendAuthResponseAsync(stream, authResponse, header.MessageId, cancellationToken);
                if (authResponse.Success)
                {
                    context.IsAuthenticated = true;
                }
                break;

            case MessageType.Request:
                // 检查认证
                if (_authOptions.RequireAuth && !context.IsAuthenticated)
                {
                    await SendAuthErrorResponseAsync(stream, header.MessageId,
                        "Not authenticated", cancellationToken);
                    return;
                }

                // 验证 Token（如果提供了）
                if (_authOptions.RequireAuth && !string.IsNullOrEmpty(header.Token))
                {
                    var validationResult = _authOptions.TokenGenerator!.ValidateToken(header.Token);
                    if (!validationResult.IsValid)
                    {
                        await SendAuthErrorResponseAsync(stream, header.MessageId,
                            $"Invalid token: {validationResult.ErrorMessage}", cancellationToken);
                        return;
                    }
                }

                var request = _serializer.DeserializeRequest(content);
                var response = await ProcessRequestAsync(request);
                await SendResponseAsync(stream, response, header.MessageId, cancellationToken);

                // 更新会话活动时间
                context.Session?.Touch();
                break;

            case MessageType.Ping:
                await SendPongAsync(stream, header.MessageId, cancellationToken);
                break;

            case MessageType.AuthRenew:
                // Token 续期请求
                if (_authOptions.RequireAuth && context.Session != null)
                {
                    var newToken = _authOptions.TokenGenerator!.GenerateToken(
                        context.Session.ClientId,
                        context.Session.Claims,
                        _authOptions.TokenLifetime);
                    var newExpiresAt = DateTime.UtcNow.Add(_authOptions.TokenLifetime);
                    context.Session.SetAuthInfo(newToken, newExpiresAt);
                    _sessionManager!.UpdateTokenMapping(newToken, context.Session.SessionId);

                    await SendAuthResponseAsync(stream, new AuthResponseMessage
                    {
                        Success = true,
                        ClientId = context.Session.ClientId,
                        Token = newToken,
                        ExpiresAt = newExpiresAt
                    }, header.MessageId, cancellationToken);
                }
                break;
        }
    }

    private async Task<AuthResponseMessage> ProcessAuthRequestAsync(AuthRequestMessage request, ClientConnectionContext context)
    {
        if (!_authOptions.RequireAuth)
        {
            return new AuthResponseMessage
            {
                Success = true,
                ClientId = request.ClientId ?? Guid.NewGuid().ToString("N"),
                Message = "Authentication not required"
            };
        }

        // 尝试恢复会话（重连场景）
        if (request.IsReconnect && !string.IsNullOrEmpty(request.PreviousToken))
        {
            var restoredSession = _sessionManager!.TryRestoreSession(
                request.ClientId ?? "unknown",
                request.PreviousToken);

            if (restoredSession != null)
            {
                // 生成新的 Token
                var newToken = _authOptions.TokenGenerator!.GenerateToken(
                    restoredSession.ClientId,
                    restoredSession.Claims,
                    _authOptions.TokenLifetime);
                var newExpiresAt = DateTime.UtcNow.Add(_authOptions.TokenLifetime);

                restoredSession.SetAuthInfo(newToken, newExpiresAt);
                _sessionManager.UpdateTokenMapping(newToken, restoredSession.SessionId);

                context.Session = restoredSession;

                return new AuthResponseMessage
                {
                    Success = true,
                    ClientId = restoredSession.ClientId,
                    Token = newToken,
                    ExpiresAt = newExpiresAt,
                    IsSessionRestored = true,
                    Message = "Session restored successfully"
                };
            }

            // 会话恢复失败，尝试重新认证
            _logger.LogWarning("Session restore failed for client {ClientId}, attempting re-authentication",
                request.ClientId);
        }

        // 执行认证
        var authResult = await _authOptions.AuthProvider!.ValidateAsync(request.Credentials);

        if (!authResult.Success)
        {
            return new AuthResponseMessage
            {
                Success = false,
                ErrorMessage = authResult.ErrorMessage ?? "Authentication failed"
            };
        }

        // 创建新会话
        var session = _sessionManager!.CreateSession(authResult.ClientId ?? Guid.NewGuid().ToString("N"));

        // 复制声明信息
        if (authResult.Claims != null)
        {
            foreach (var claim in authResult.Claims)
            {
                session.Claims[claim.Key] = claim.Value;
            }
        }

        // 生成 Token
        var token = _authOptions.TokenGenerator!.GenerateToken(
            session.ClientId,
            session.Claims,
            _authOptions.TokenLifetime);
        var expiresAt = DateTime.UtcNow.Add(_authOptions.TokenLifetime);

        session.SetAuthInfo(token, expiresAt);
        _sessionManager.UpdateTokenMapping(token, session.SessionId);

        context.Session = session;

        return new AuthResponseMessage
        {
            Success = true,
            ClientId = session.ClientId,
            Token = token,
            ExpiresAt = expiresAt,
            IsSessionRestored = false,
            Message = "Authentication successful"
        };
    }

    private async Task<ResponseMessage> ProcessRequestAsync(RequestMessage request)
    {
        try
        {
            // 首先尝试从旧版处理器查找
            if (_handlers.TryGetValue(request.Method, out var handler))
            {
                return await handler(request);
            }

            // 尝试从服务注册表查找（新API）
            if (!string.IsNullOrEmpty(request.ServiceType))
            {
                var result = await ServiceRegistry.InvokeAsync(
                    request.ServiceType,
                    request.Method,
                    request.Parameters,
                    request.ParameterTypes);

                if (result.Success)
                {
                    return new ResponseMessage
                    {
                        Success = true,
                        Data = result.Value,
                        DataType = result.ValueType?.FullName,
                        IsVoid = result.IsVoid
                    };
                }
                else if (result.Exception != null)
                {
                    return new ResponseMessage
                    {
                        Success = false,
                        Error = result.Error,
                        ExceptionType = result.Exception.GetType().FullName,
                        StackTrace = result.Exception.StackTrace
                    };
                }
                else
                {
                    return new ResponseMessage
                    {
                        Success = false,
                        Error = result.Error
                    };
                }
            }

            return new ResponseMessage
            {
                Success = false,
                Error = $"Method '{request.Method}' not found"
            };
        }
        catch (Exception ex)
        {
            return new ResponseMessage
            {
                Success = false,
                Error = $"Handler error: {ex.Message}",
                ExceptionType = ex.GetType().FullName,
                StackTrace = ex.StackTrace
            };
        }
    }

    private async Task SendResponseAsync(NetworkStream stream, ResponseMessage response, long messageId,
        CancellationToken cancellationToken)
    {
        var responseData = _serializer.SerializeResponse(response);
        var header = new MessageHeader
        {
            Type = response.Success ? MessageType.Response : MessageType.Error,
            MessageId = messageId,
            ContentLength = responseData.Length
        };

        await NetworkService.WriteMessageAsync(stream, header, responseData, cancellationToken);
    }

    private async Task SendAuthResponseAsync(NetworkStream stream, AuthResponseMessage response,
        long messageId, CancellationToken cancellationToken)
    {
        var responseData = _serializer.SerializeAuthResponse(response);
        var header = new MessageHeader
        {
            Type = response.Success ? MessageType.AuthResponse : MessageType.Error,
            MessageId = messageId,
            ContentLength = responseData.Length
        };

        await NetworkService.WriteMessageAsync(stream, header, responseData, cancellationToken);
    }

    private async Task SendAuthErrorResponseAsync(NetworkStream stream, long messageId,
        string error, CancellationToken cancellationToken)
    {
        var errorResponse = new AuthResponseMessage
        {
            Success = false,
            ErrorMessage = error
        };
        await SendAuthResponseAsync(stream, errorResponse, messageId, cancellationToken);
    }

    private async Task SendErrorResponseAsync(NetworkStream stream, long messageId,
        string error, CancellationToken cancellationToken)
    {
        var errorData = System.Text.Encoding.UTF8.GetBytes(error);
        var header = new MessageHeader
        {
            Type = MessageType.Error,
            MessageId = messageId,
            ContentLength = errorData.Length
        };

        await NetworkService.WriteMessageAsync(stream, header, errorData, cancellationToken);
    }

    private async Task SendPongAsync(NetworkStream stream, long messageId,
        CancellationToken cancellationToken)
    {
        var header = new MessageHeader
        {
            Type = MessageType.Pong,
            MessageId = messageId,
            ContentLength = 0
        };

        await NetworkService.WriteMessageAsync(stream, header, null, cancellationToken);
    }

    private void CheckDisposed()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 1) return;
        
        throw new ObjectDisposedException(nameof(TcpRpcServer));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(15));
        }
        catch
        {
            // 忽略停止时的错误
        }

        _sessionManager?.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}