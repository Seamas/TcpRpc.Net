using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;

namespace TcpRpc.Client;

/// <summary>
/// 连接池 - 管理多个TcpRpcClient连接
/// </summary>
public class ConnectionPool : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _maxSize;
    private readonly TimeSpan _maxIdleTime;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMessageSerializer _messageSerializer;
    private readonly ClientAuthOptions _authOptions;
    private readonly ConcurrentBag<PooledConnection> _availableConnections = new();
    private readonly ConcurrentDictionary<TcpRpcClient, PooledConnection> _allConnections = new();
    private int _connectionCount;
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public ConnectionPool(string host, int port, IMessageSerializer messageSerializer, ILoggerFactory loggerFactory,
        int maxSize = 10, ClientAuthOptions? authOptions = null, TimeSpan? maxIdleTime = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _maxSize = maxSize;
        _messageSerializer = messageSerializer;
        _maxIdleTime = maxIdleTime ?? TimeSpan.FromMinutes(5);
        _loggerFactory = loggerFactory;
        _authOptions = authOptions ?? new ClientAuthOptions();
        _semaphore = new SemaphoreSlim(maxSize, maxSize);
        _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public async Task<TcpRpcClient> AcquireAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // 尝试从池中获取可用连接
            while (_availableConnections.TryTake(out var pooledConnection))
            {
                if (pooledConnection.Client.State == RpcClientState.Connected)
                {
                    pooledConnection.LastUsedTime = DateTime.UtcNow;
                    pooledConnection.IsInUse = true;
                    return pooledConnection.Client;
                }

                // 连接已断开，清理
                await DisposeConnectionAsync(pooledConnection);
            }

            // 创建新连接
            if (Interlocked.Increment(ref _connectionCount) <= _maxSize)
            {
                var client = await CreateNewConnectionAsync();
                var newPooledConnection = new PooledConnection
                {
                    Client = client,
                    LastUsedTime = DateTime.UtcNow,
                    IsInUse = true
                };
                _allConnections[client] = newPooledConnection;
                return client;
            }
            else
            {
                Interlocked.Decrement(ref _connectionCount);
                throw new InvalidOperationException("Connection pool exhausted");
            }
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 释放连接回池中
    /// </summary>
    public void Release(TcpRpcClient client)
    {
        if (_allConnections.TryGetValue(client, out var pooledConnection))
        {
            pooledConnection.IsInUse = false;
            pooledConnection.LastUsedTime = DateTime.UtcNow;
            _availableConnections.Add(pooledConnection);
        }

        _semaphore.Release();
    }

    /// <summary>
    /// 获取或创建代理
    /// </summary>
    public async Task<T> GetProxyAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        var client = await AcquireAsync(cancellationToken);
        var proxy = RpcClientProxy.Create<T>(client, _messageSerializer, _loggerFactory);
        return proxy;
    }

    private async Task<TcpRpcClient> CreateNewConnectionAsync()
    {
        var client = new TcpRpcClient(_host, _port, _messageSerializer, _loggerFactory, _authOptions);
        await client.ConnectAndAuthenticateAsync();
        return client;
    }

    private async Task DisposeConnectionAsync(PooledConnection connection)
    {
        try
        {
            await connection.Client.DisconnectAsync();
            connection.Client.Dispose();
        }
        catch
        {
            // 忽略清理错误
        }
        Interlocked.Decrement(ref _connectionCount);
    }

    private void CleanupIdleConnections(object? state)
    {
        var now = DateTime.UtcNow;
        var connectionsToRemove = new List<PooledConnection>();

        foreach (var connection in _allConnections.Values)
        {
            if (!connection.IsInUse && now - connection.LastUsedTime > _maxIdleTime)
            {
                if (_availableConnections.TryTake(out var taken) && taken == connection)
                {
                    connectionsToRemove.Add(connection);
                }
                else if (taken != null)
                {
                    _availableConnections.Add(taken);
                }
            }
        }

        foreach (var connection in connectionsToRemove)
        {
            _allConnections.TryRemove(connection.Client, out _);
            DisposeConnectionAsync(connection).ConfigureAwait(false);
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        _semaphore.Dispose();

        foreach (var connection in _allConnections.Values)
        {
            connection.Client.Dispose();
        }

        _allConnections.Clear();
        _availableConnections.Clear();
    }

    private class PooledConnection
    {
        public required TcpRpcClient Client { get; init; }
        public DateTime LastUsedTime { get; set; }
        public bool IsInUse { get; set; }
    }
}
