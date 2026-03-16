using TcpRpc.Server.Auth;

namespace TcpRpc.Server;

/// <summary>
/// 简单认证提供者 - 基于用户名/密码的认证
/// </summary>
public class SimpleAuthProvider : IAuthProvider
{
    private readonly Dictionary<string, string> _users;
    private readonly Func<string, Dictionary<string, string>>? _claimsFactory;

    /// <summary>
    /// 创建简单认证提供者
    /// </summary>
    /// <param name="users">用户名密码字典</param>
    /// <param name="claimsFactory">声明工厂，用于根据用户名生成附加声明</param>
    public SimpleAuthProvider(Dictionary<string, string> users, Func<string, Dictionary<string, string>>? claimsFactory = null)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _claimsFactory = claimsFactory;
    }

    public Task<AuthResult> ValidateAsync(Dictionary<string, string>? credentials, CancellationToken cancellationToken = default)
    {
        if (credentials == null ||
            !credentials.TryGetValue("username", out var username) ||
            !credentials.TryGetValue("password", out var password))
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Missing username or password"
            });
        }

        if (!_users.TryGetValue(username, out var expectedPassword) || expectedPassword != password)
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Invalid username or password"
            });
        }

        var claims = _claimsFactory?.Invoke(username);

        return Task.FromResult(new AuthResult
        {
            Success = true,
            ClientId = username,
            Claims = claims
        });
    }
}

/// <summary>
/// 允许匿名访问的认证提供者（用于开发测试）
/// </summary>
public class AnonymousAuthProvider : IAuthProvider
{
    public Task<AuthResult> ValidateAsync(Dictionary<string, string>? credentials, CancellationToken cancellationToken = default)
    {
        var clientId = credentials?.TryGetValue("clientId", out var id) == true
            ? id
            : Guid.NewGuid().ToString("N");

        return Task.FromResult(new AuthResult
        {
            Success = true,
            ClientId = clientId,
            Claims = credentials ?? new Dictionary<string, string>()
        });
    }
}
