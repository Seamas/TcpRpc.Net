using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;
using TcpRpc.Message;

namespace TcpRpc.Server;

/// <summary>
/// TCP RPC服务器 - 支持服务注册和拦截器
/// </summary>
public class TcpRpcServer : IDisposable
{
    private readonly int _port;
    private readonly IMessageSerializer _serializer;
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

    /// <summary>
    /// 服务注册表
    /// </summary>
    public ServiceRegistry ServiceRegistry { get; }

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

    public TcpRpcServer(int port, IMessageSerializer serializer, ILoggerFactory factory)
    {
        _port = port;
        _serializer = serializer;
        _logger = factory.CreateLogger<TcpRpcServer>();
        ServiceRegistry = new ServiceRegistry(factory);
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

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var headerBuffer = new byte[MessageHeader.HeaderSize];

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        // Read header with timeout
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, timeoutCts.Token);

                        var bytesRead = 0;
                        while (bytesRead < MessageHeader.HeaderSize)
                        {
                            var read = await stream.ReadAsync(headerBuffer.AsMemory(bytesRead, 
                                MessageHeader.HeaderSize - bytesRead), linkedCts.Token);
                            if (read == 0)
                            {
                                _logger.LogInformation("Client {ClientId} disconnected", clientId);
                                return;
                            }
                            bytesRead += read;
                        }

                        var header = MessageHeader.Deserialize(headerBuffer);

                        // 检查消息大小限制
                        if (header.ContentLength > 100 * 1024 * 1024) // 100MB limit
                        {
                            await SendErrorResponseAsync(stream, header.MessageId, 
                                "Message too large", cancellationToken);
                            continue;
                        }

                        // Read content
                        var contentBuffer = new byte[header.ContentLength];
                        bytesRead = 0;
                        while (bytesRead < header.ContentLength)
                        {
                            var read = await stream.ReadAsync(contentBuffer.AsMemory(bytesRead, 
                                header.ContentLength - bytesRead), linkedCts.Token);
                            if (read == 0)
                            {
                                _logger.LogInformation("Client {ClientId} disconnected during content read", clientId);
                                return;
                            }
                            bytesRead += read;
                        }

                        // Process message
                        await ProcessClientMessageAsync(stream, header, contentBuffer, cancellationToken);
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

    private async Task ProcessClientMessageAsync(NetworkStream stream, MessageHeader header, 
        byte[] content, CancellationToken cancellationToken)
    {
        switch (header.Type)
        {
            case MessageType.Request:
                var request = _serializer.DeserializeRequest(content);
                var response = await ProcessRequestAsync(request);
                await SendResponseAsync(stream, response, header.MessageId, cancellationToken);
                break;

            case MessageType.Ping:
                await SendPongAsync(stream, header.MessageId, cancellationToken);
                break;
        }
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
        var responseHeader = new MessageHeader
        {
            Type = response.Success ? MessageType.Response : MessageType.Error,
            MessageId = messageId,
            ContentLength = responseData.Length
        };

        var headerBytes = responseHeader.Serialize();
        await stream.WriteAsync(headerBytes,  cancellationToken);
        await stream.WriteAsync(responseData, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

        var headerBytes = header.Serialize();
        await stream.WriteAsync(headerBytes,  cancellationToken);
        await stream.WriteAsync(errorData,  cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

        var headerBytes = header.Serialize();
        await stream.WriteAsync(headerBytes,  cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}