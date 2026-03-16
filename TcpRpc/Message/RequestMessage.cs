namespace TcpRpc.Message;

public class RequestMessage
{
    public long MessageId { get; init; }
    public required string Method { get; init; } 
    public required byte[] Data { get; init; }
}