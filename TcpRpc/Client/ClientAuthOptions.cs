namespace TcpRpc.Client;

/// <summary>
/// 客户端认证配置选项
/// </summary>
public class ClientAuthOptions
{
    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 重连策略
    /// </summary>
    public IReconnectPolicy ReconnectPolicy { get; set; } = new ExponentialBackoffReconnectPolicy();

    /// <summary>
    /// 认证凭证
    /// </summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>
    /// 客户端标识（用于重连时恢复会话）
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 认证超时时间
    /// </summary>
    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
