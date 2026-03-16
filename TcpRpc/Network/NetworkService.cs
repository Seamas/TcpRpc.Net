using System.Buffers.Binary;
using System.Net.Sockets;
using TcpRpc.Message;

namespace TcpRpc.Network;

public static class NetworkService
{
    
    public static async Task WriteMessageAsync(NetworkStream stream, MessageHeader header, byte[]? data, CancellationToken cancellationToken)
    {
        // 强制设置数据长度
        header.ContentLength = data?.Length ?? 0;
        
        var headerBytes = header.Serialize();
        
        var headerSize = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(headerSize, headerBytes.Length);
        
        await stream.WriteAsync(headerSize, cancellationToken);
        await stream.WriteAsync(headerBytes, cancellationToken);
        if (data != null)
        {
            await stream.WriteAsync(data, cancellationToken);
        }
        
        await stream.FlushAsync(cancellationToken);
    }
    
    public static async Task<MessageHeader?> ReadMessageHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var headerSizeBuffer = await ReadBytesAsync(stream, 4, cancellationToken);
        if (headerSizeBuffer == null || headerSizeBuffer.Length != 4)
        {
            return null;
        }
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(headerSizeBuffer);

        // 防御性检查：头部长度必须为正数且合理
        if (headerSize <= 0 || headerSize > 1024 * 1024) // 最大1MB
        {
            throw new InvalidDataException($"Invalid header size: {headerSize}");
        }

        var readBytesAsync = await ReadBytesAsync(stream, headerSize, cancellationToken);
        if (readBytesAsync == null || readBytesAsync.Length != headerSize)
        {
            return null;
        }

        return MessageHeader.Deserialize(readBytesAsync);
    }
    
    
    public static async Task<byte[]?> ReadBytesAsync(NetworkStream stream, int contentLength, CancellationToken cancellationToken)
    {
        if (contentLength < 0)
        {
            throw new InvalidDataException($"Content length cannot be negative: {contentLength}");
        }

        if (contentLength == 0)
        {
            return null;
        }

        if (contentLength > 100 * 1024)
        {
            const int bufferSize = 8196;
            var buffer = new byte[bufferSize];
            var bytesRead = 0;
            while (bytesRead < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken);
                if (read == 0)
                {
                    return null;
                }
                bytesRead += read;
            }
            return [];
        }
        else
        {
            var buffer = new byte[contentLength];
            var bytesRead = 0;
            while (bytesRead < contentLength)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, contentLength - bytesRead), cancellationToken);
                if (read == 0)
                {
                    return null;
                }
                bytesRead += read;
            }
            return buffer;
        }
    }
}