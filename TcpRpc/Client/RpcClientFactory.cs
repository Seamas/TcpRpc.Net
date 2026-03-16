using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;

namespace TcpRpc.Client;

/// <summary>
/// RPC客户端工厂 - 简化代理创建
/// </summary>
public class RpcClientFactory : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ConnectionPool? _connectionPool;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMessageSerializer _messageSerializer;
    private readonly ClientAuthOptions _authOptions;

    public RpcClientFactory(string host, int port, IMessageSerializer messageSerializer, ILoggerFactory loggerFactory,
        bool usePool = true, int poolSize = 10, ClientAuthOptions? authOptions = null)
    {
        _host = host;
        _port = port;
        _messageSerializer = messageSerializer;
        _loggerFactory = loggerFactory;
        _authOptions = authOptions ?? new ClientAuthOptions();

        if (usePool)
        {
            _connectionPool = new ConnectionPool(host, port, messageSerializer, loggerFactory, poolSize, _authOptions);
        }
    }

    /// <summary>
    /// 创建服务代理
    /// </summary>
    public async Task<T> CreateProxyAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        if (_connectionPool != null)
        {
            return await _connectionPool.GetProxyAsync<T>(cancellationToken);
        }

        var client = new TcpRpcClient(_host, _port, _messageSerializer, _loggerFactory, _authOptions);
        await client.ConnectAndAuthenticateAsync(cancellationToken);
        return RpcClientProxy.Create<T>(client, _messageSerializer, _loggerFactory);
    }

    /// <summary>
    /// 创建直接连接的客户端
    /// </summary>
    public async Task<TcpRpcClient> CreateClientAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionPool != null)
        {
            return await _connectionPool.AcquireAsync(cancellationToken);
        }

        var client = new TcpRpcClient(_host, _port, _messageSerializer, _loggerFactory, _authOptions);
        await client.ConnectAndAuthenticateAsync(cancellationToken);
        return client;
    }

    /// <summary>
    /// 创建客户端（仅连接，不认证）
    /// </summary>
    public async Task<TcpRpcClient> CreateClientWithoutAuthAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionPool != null)
        {
            return await _connectionPool.AcquireAsync(cancellationToken);
        }

        var client = new TcpRpcClient(_host, _port, _messageSerializer, _loggerFactory, _authOptions);
        await client.ConnectAsync(cancellationToken);
        return client;
    }

    /// <summary>
    /// 释放连接回池中
    /// </summary>
    public void ReleaseClient(TcpRpcClient client)
    {
        _connectionPool?.Release(client);
    }

    /// <summary>
    /// 更新认证凭证
    /// </summary>
    public void SetCredentials(Dictionary<string, string> credentials)
    {
        _authOptions.Credentials.Clear();
        foreach (var kvp in credentials)
        {
            _authOptions.Credentials[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// 设置客户端标识
    /// </summary>
    public void SetClientId(string clientId)
    {
        _authOptions.ClientId = clientId;
    }

    public void Dispose()
    {
        _connectionPool?.Dispose();
    }
}