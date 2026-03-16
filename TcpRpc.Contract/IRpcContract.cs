namespace TcpRpc.Contract;

/// <summary>
/// 示例RPC契约接口 - 展示多种方法签名
/// </summary>
public interface IRpcContract
{
    /// <summary>
    /// 简单调用 - 旧版兼容
    /// </summary>
    RpcResponse Call(RpcRequest request);

    /// <summary>
    /// 多参数调用
    /// </summary>
    int Add(int a, int b);

    /// <summary>
    /// 字符串处理
    /// </summary>
    string Greet(string name, string greeting);

    /// <summary>
    /// Void返回
    /// </summary>
    void LogMessage(string message, LogLevel level);

    /// <summary>
    /// 异步方法
    /// </summary>
    Task<string> ProcessAsync(string input, int delayMs);

    Task ProcessAsyncWithoutReturn(string input, int deleyMs);

    /// <summary>
    /// 复杂对象
    /// </summary>
    UserInfo GetUserInfo(int userId, bool includeDetails);

    /// <summary>
    /// 抛出异常
    /// </summary>
    void ThrowException(string message);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class UserInfo
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}