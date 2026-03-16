using System.Buffers.Binary;

namespace TcpRpc.Message;

/// <summary>
/// 消息头 - 扩展支持返回类型标志
/// </summary>
public class MessageHeader
{
    public const int HeaderSize = 14; // 1 (Type) + 8 (MessageId) + 4 (ContentLength) + 1 (Flags)

    public MessageType Type { get; set; }
    public long MessageId { get; set; }
    public int ContentLength { get; set; }
    /// <summary>
    /// 标志位: 0x01=Void返回, 0x02=有异常, 0x04=压缩
    /// </summary>
    public byte Flags { get; set; }

    public bool IsVoid => (Flags & 0x01) != 0;
    public bool HasException => (Flags & 0x02) != 0;
    public bool IsCompressed => (Flags & 0x04) != 0;

    public void SetVoid(bool value)
    {
        if (value) Flags |= 0x01;
        else Flags &= unchecked((byte)~0x01);
    }

    public void SetHasException(bool value)
    {
        if (value) Flags |= 0x02;
        else Flags &= unchecked((byte)~0x02);
    }

    public byte[] Serialize()
    {
        var buffer = new byte[HeaderSize];
        buffer[0] = (byte)Type;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(1), MessageId);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(9), ContentLength);
        buffer[13] = Flags;
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
            ContentLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(9)),
            Flags = buffer[13]
        };
    }
}