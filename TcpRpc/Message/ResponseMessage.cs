namespace TcpRpc.Message;

public class ResponseMessage
{
    public long MessageId { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? Data { get; init; }
}