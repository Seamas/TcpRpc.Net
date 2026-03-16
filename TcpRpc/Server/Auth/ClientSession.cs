namespace TcpRpc.Server.Auth;

/// <summary>
/// 客户端会话信息
/// </summary>
public class ClientSession
{
    /// <summary>
    /// 会话标识
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// 当前认证 Token
    /// </summary>
    public string? CurrentToken { get; set; }

    /// <summary>
    /// Token 过期时间
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// 会话创建时间
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// 是否已认证
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(CurrentToken);

    /// <summary>
    /// 关联的连接标识（用于识别重连）
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// 附加声明信息
    /// </summary>
    public Dictionary<string, string> Claims { get; }

    /// <summary>
    /// 自定义数据存储
    /// </summary>
    public Dictionary<string, object> Items { get; }

    public ClientSession(string sessionId, string clientId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        CreatedAt = DateTime.UtcNow;
        LastActivityAt = CreatedAt;
        Claims = new Dictionary<string, string>();
        Items = new Dictionary<string, object>();
    }

    /// <summary>
    /// 更新活动时间
    /// </summary>
    public void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 检查 Token 是否过期
    /// </summary>
    public bool IsTokenExpired()
    {
        if (!TokenExpiresAt.HasValue) return true;
        return DateTime.UtcNow >= TokenExpiresAt.Value;
    }

    /// <summary>
    /// 更新认证信息
    /// </summary>
    public void SetAuthInfo(string token, DateTime expiresAt)
    {
        CurrentToken = token;
        TokenExpiresAt = expiresAt;
        Touch();
    }
}
