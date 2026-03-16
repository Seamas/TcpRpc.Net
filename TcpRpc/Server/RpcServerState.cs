namespace TcpRpc.Server;

public enum RpcServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}