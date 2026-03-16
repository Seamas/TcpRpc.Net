namespace TcpRpc.Message;

/// <summary>
/// 认证响应消息
/// </summary>
public class AuthResponseMessage
{
    /// <summary>
    /// 是否认证成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 错误信息（认证失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 分配的客户端标识
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// 认证 Token
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Token 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// 是否为会话恢复（重连成功）
    /// </summary>
    public bool IsSessionRestored { get; init; }

    /// <summary>
    /// 附加消息（如认证成功/失败说明）
    /// </summary>
    public string? Message { get; init; }
}
