namespace TcpRpc.Contract;

/// <summary>
/// RPC请求 - 旧版兼容
/// </summary>
public class RpcRequest
{
    public required string Message { get; set; }
}

