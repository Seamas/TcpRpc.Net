namespace TcpRpc.Message;

/// <summary>
/// RPC请求消息 - 支持多参数调用
/// </summary>
public class RequestMessage
{
    public required string Method { get; init; }
    public required string ServiceType { get; init; }
    /// <summary>
    /// 参数数组，支持多参数传递
    /// </summary>
    public object?[]? Parameters { get; init; }
    /// <summary>
    /// 参数类型名称数组，用于反序列化时类型还原
    /// </summary>
    public string[]? ParameterTypes { get; init; }
}