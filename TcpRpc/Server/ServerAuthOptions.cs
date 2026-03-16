using TcpRpc.Server.Auth;

namespace TcpRpc.Server;

/// <summary>
/// 服务端认证配置选项
/// </summary>
public class ServerAuthOptions
{
    /// <summary>
    /// 是否启用认证
    /// </summary>
    public bool RequireAuth { get; set; } = true;

    /// <summary>
    /// Token 有效期
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 会话超时时间（无活动后过期）
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 会话清理间隔
    /// </summary>
    public TimeSpan SessionCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 认证提供者
    /// </summary>
    public required IAuthProvider AuthProvider { get; set; }

    /// <summary>
    /// Token 生成器
    /// </summary>
    public IAuthTokenGenerator? TokenGenerator { get; set; }
}
