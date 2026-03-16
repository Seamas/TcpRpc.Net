namespace TcpRpc;

/// <summary>
/// RPC调用异常 - 用于在客户端重建服务端异常
/// </summary>
public class RpcException : Exception
{
    /// <summary>
    /// 服务端原始异常类型
    /// </summary>
    public string? RemoteExceptionType { get; }

    /// <summary>
    /// 服务端原始堆栈
    /// </summary>
    public string? RemoteStackTrace { get; }

    public RpcException(string message) : base(message)
    {
    }

    public RpcException(string message, string? remoteExceptionType, string? remoteStackTrace)
        : base(message)
    {
        RemoteExceptionType = remoteExceptionType;
        RemoteStackTrace = remoteStackTrace;
    }

    public override string StackTrace => RemoteStackTrace ?? base.StackTrace;

    public override string ToString()
    {
        var type = RemoteExceptionType ?? "RpcException";
        return $"{type}: {Message}\n{StackTrace}";
    }
}
