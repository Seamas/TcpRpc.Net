namespace TcpRpc.Server.Auth;

/// <summary>
/// Token 验证结果
/// </summary>
public class TokenValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Token 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// 声明信息
    /// </summary>
    public Dictionary<string, string>? Claims { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Token 生成器接口
/// </summary>
public interface IAuthTokenGenerator
{
    /// <summary>
    /// 生成 Token
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    /// <param name="claims">附加声明</param>
    /// <param name="lifetime">Token 有效期</param>
    /// <returns>生成的 Token</returns>
    string GenerateToken(string clientId, Dictionary<string, string>? claims, TimeSpan lifetime);

    /// <summary>
    /// 验证 Token
    /// </summary>
    /// <param name="token">Token 字符串</param>
    /// <returns>验证结果</returns>
    TokenValidationResult ValidateToken(string token);
}
