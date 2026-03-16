using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TcpRpc.Server.Auth;

/// <summary>
/// 简单的基于 HMAC 的 Token 生成器
/// 格式: Base64(Header).Base64(Payload).Base64(Signature)
/// </summary>
public class SimpleTokenGenerator : IAuthTokenGenerator
{
    private readonly byte[] _secretKey;
    private readonly string _issuer;

    /// <summary>
    /// 创建简单 Token 生成器
    /// </summary>
    /// <param name="secretKey">密钥（建议至少 32 字节）</param>
    /// <param name="issuer">签发者标识</param>
    public SimpleTokenGenerator(byte[] secretKey, string issuer = "tcprpc")
    {
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _issuer = issuer;
    }

    /// <summary>
    /// 创建简单 Token 生成器（自动生成随机密钥）
    /// </summary>
    /// <param name="issuer">签发者标识</param>
    public SimpleTokenGenerator(string issuer = "tcprpc")
    {
        _secretKey = new byte[32];
        RandomNumberGenerator.Fill(_secretKey);
        _issuer = issuer;
    }

    public string GenerateToken(string clientId, Dictionary<string, string>? claims, TimeSpan lifetime)
    {
        var header = new Dictionary<string, string>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        var now = DateTime.UtcNow;
        var payload = new Dictionary<string, string>
        {
            ["sub"] = clientId,
            ["iss"] = _issuer,
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
            ["exp"] = new DateTimeOffset(now.Add(lifetime)).ToUnixTimeSeconds().ToString()
        };

        // 添加自定义声明
        if (claims != null)
        {
            foreach (var claim in claims)
            {
                if (!payload.ContainsKey(claim.Key))
                {
                    payload[claim.Key] = claim.Value;
                }
            }
        }

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signature = ComputeSignature(headerBase64, payloadBase64);
        var signatureBase64 = Base64UrlEncode(signature);

        return $"{headerBase64}.{payloadBase64}.{signatureBase64}";
    }

    public TokenValidationResult ValidateToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return new TokenValidationResult { IsValid = false, ErrorMessage = "Invalid token format" };
            }

            // 验证签名
            var expectedSignature = ComputeSignature(parts[0], parts[1]);
            var actualSignature = Base64UrlDecode(parts[2]);

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
            {
                return new TokenValidationResult { IsValid = false, ErrorMessage = "Invalid signature" };
            }

            // 解析 payload
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson);

            if (payload == null)
            {
                return new TokenValidationResult { IsValid = false, ErrorMessage = "Invalid payload" };
            }

            // 检查过期时间
            if (payload.TryGetValue("exp", out var expStr) &&
                long.TryParse(expStr, out var exp))
            {
                var expTime = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                if (expTime < DateTime.UtcNow)
                {
                    return new TokenValidationResult { IsValid = false, ErrorMessage = "Token expired" };
                }

                // 提取声明（排除标准字段）
                var claims = payload
                    .Where(p => p.Key != "sub" && p.Key != "iss" && p.Key != "iat" && p.Key != "exp")
                    .ToDictionary(p => p.Key, p => p.Value);

                return new TokenValidationResult
                {
                    IsValid = true,
                    ClientId = payload.GetValueOrDefault("sub"),
                    ExpiresAt = expTime,
                    Claims = claims
                };
            }

            return new TokenValidationResult { IsValid = false, ErrorMessage = "Missing expiration" };
        }
        catch (Exception ex)
        {
            return new TokenValidationResult { IsValid = false, ErrorMessage = $"Validation error: {ex.Message}" };
        }
    }

    private byte[] ComputeSignature(string header, string payload)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var data = Encoding.UTF8.GetBytes($"{header}.{payload}");
        return hmac.ComputeHash(data);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}
