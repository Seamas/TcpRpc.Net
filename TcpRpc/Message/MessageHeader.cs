namespace TcpRpc.Message;

public class MessageHeader
{
    public const int HeaderSize = 17; // 1 + 8 + 8

    public MessageType Type { get; set; }
    public long MessageId { get; set; }
    public long ContentLength { get; set; }

    public byte[] Serialize()
    {
        var buffer = new byte[HeaderSize];
        buffer[0] = (byte)Type;
        BitConverter.GetBytes(MessageId).CopyTo(buffer, 1);
        BitConverter.GetBytes(ContentLength).CopyTo(buffer, 9);
        return buffer;
    }

    public static MessageHeader Deserialize(byte[] buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new ArgumentException($"Buffer size must be at least {HeaderSize} bytes");

        return new MessageHeader
        {
            Type = (MessageType)buffer[0],
            MessageId = BitConverter.ToInt64(buffer, 1),
            ContentLength = BitConverter.ToInt64(buffer, 9)
        };
    }
}