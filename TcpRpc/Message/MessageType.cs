namespace TcpRpc.Message;

public enum MessageType : byte
{
    Request = 0x01,
    Response = 0x02,
    Ping = 0x03,
    Pong = 0x04,
    Error = 0xFF
}