using System.Buffers.Binary;

namespace TcpRpc.Message;

public class MessageHeader
{
    public const int HeaderSize = 17; // 1 + 8 + 8

    public MessageType Type { get; set; }
    public long MessageId { get; set; }
    public int ContentLength { get; set; }

    public byte[] Serialize()
    {
        var buffer = new byte[HeaderSize];
        buffer[0] = (byte)Type;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1), MessageId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(9), ContentLength);
        return buffer;
    }

    public static MessageHeader Deserialize(byte[] buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new ArgumentException($"Buffer size must be at least {HeaderSize} bytes");

        return new MessageHeader
        {
            Type = (MessageType)buffer[0],
            MessageId = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(1)),
            ContentLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(9))
        };
    }
}