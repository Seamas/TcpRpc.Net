using System.Buffers.Binary;
using System.Text;

namespace TcpRpc.Message;

/// <summary>
/// 消息头 - 扩展支持返回类型标志
/// </summary>
public class MessageHeader
{
    // 长度1
    public MessageType Type { get; set; }
    // 长度8
    public long MessageId { get; set; }
    // 长度4
    public int ContentLength { get; set; }
    /// <summary>
    /// 长度1
    /// 标志位: 0x01=Void返回, 0x02=有异常, 0x04=压缩
    /// </summary>
    public byte Flags { get; set; }
    
    public string? Token { get; set; }

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
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, true);
        writer.Write((byte)Type);
        writer.Write(MessageId);
        writer.Write(ContentLength);
        writer.Write(Flags);
        writer.Write(Token ?? string.Empty);
        return ms.ToArray();
    }

    public static MessageHeader Deserialize(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var reader = new BinaryReader(ms, Encoding.UTF8, true);

        var readByte = (MessageType) reader.ReadByte();
        var messageId = reader.ReadInt64();
        var contentLength = reader.ReadInt32();
        var flags = reader.ReadByte();
        var token = reader.ReadString();
        
        return new MessageHeader
        {
            Type = readByte,
            MessageId = messageId,
            ContentLength = contentLength,
            Flags = flags,
            Token = token
        };
    }
}