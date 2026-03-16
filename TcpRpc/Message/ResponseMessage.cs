namespace TcpRpc.Message;

/// <summary>
/// RPC响应消息 - 支持Void返回和异常传播
/// </summary>
public class ResponseMessage
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    /// <summary>
    /// 异常类型全名，用于客户端重建异常
    /// </summary>
    public string? ExceptionType { get; init; }
    /// <summary>
    /// 异常堆栈信息
    /// </summary>
    public string? StackTrace { get; init; }
    /// <summary>
    /// 返回值数据
    /// </summary>
    public object? Data { get; init; }
    /// <summary>
    /// 返回值类型名称
    /// </summary>
    public string? DataType { get; init; }
    /// <summary>
    /// 是否为Void返回
    /// </summary>
    public bool IsVoid { get; init; }
}