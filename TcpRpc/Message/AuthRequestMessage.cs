namespace TcpRpc.Message;

/// <summary>
/// 认证请求消息
/// </summary>
public class AuthRequestMessage
{
    /// <summary>
    /// 客户端标识（用于重连时恢复会话）
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// 之前的认证 Token（重连时使用）
    /// </summary>
    public string? PreviousToken { get; init; }

    /// <summary>
    /// 认证凭证（首次认证时使用）
    /// </summary>
    public Dictionary<string, string>? Credentials { get; init; }

    /// <summary>
    /// 是否为重连请求
    /// </summary>
    public bool IsReconnect => !string.IsNullOrEmpty(PreviousToken);
}
