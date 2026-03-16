namespace TcpRpc.Message;

public enum MessageType : byte
{
    Request = 0x01,
    Response = 0x02,
    Ping = 0x03,
    Pong = 0x04,
    AuthRequest = 0x05,
    AuthResponse = 0x06,
    AuthRenew = 0x07,
    Error = 0xFF
}