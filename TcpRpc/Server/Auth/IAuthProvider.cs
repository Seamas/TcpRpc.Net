namespace TcpRpc.Server.Auth;

/// <summary>
/// 认证结果
/// </summary>
public class AuthResult
{
    /// <summary>
    /// 是否认证成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// 附加声明信息（将包含在 Token 中）
    /// </summary>
    public Dictionary<string, string>? Claims { get; init; }
}

/// <summary>
/// 认证提供者接口 - 用于验证客户端凭证
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// 验证客户端凭证
    /// </summary>
    /// <param name="credentials">凭证字典</param>
    /// <returns>认证结果</returns>
    Task<AuthResult> ValidateAsync(Dictionary<string, string>? credentials, CancellationToken cancellationToken = default);
}
