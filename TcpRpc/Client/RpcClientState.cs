namespace TcpRpc.Client;

public enum RpcClientState
{
    Created,
    Connecting,
    Connected,
    Disconnecting,
    Disconnected,
    Faulted
}