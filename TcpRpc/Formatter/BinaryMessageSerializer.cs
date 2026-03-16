using System.Text;
using System.Text.Json;
using TcpRpc.Message;

namespace TcpRpc.Formatter;

/// <summary>
/// 二进制消息序列化器 - 跨进程可用
/// 格式: [长度前缀][类型标记][数据]
/// </summary>
public class BinaryMessageSerializer : IMessageSerializer
{
    private static readonly Encoding Encoding = Encoding.UTF8;

    // 类型标记
    private const byte TypeNull = 0;
    private const byte TypeString = 1;
    private const byte TypeInt32 = 2;
    private const byte TypeInt64 = 3;
    private const byte TypeDouble = 4;
    private const byte TypeBool = 5;
    private const byte TypeDateTime = 6;
    private const byte TypeByteArray = 7;
    private const byte TypeStringArray = 8;
    private const byte TypeObjectArray = 9;
    private const byte TypeObject = 10;

    #region RequestMessage

    public byte[] SerializeRequest(RequestMessage request)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding))
        {
            // ServiceType
            WriteString(writer, request.ServiceType);

            // Method
            WriteString(writer, request.Method);

            // Parameters
            WriteObjectArray(writer, request.Parameters);

            // ParameterTypes
            WriteStringArray(writer, request.ParameterTypes);
        }
        return ms.ToArray();
    }

    public RequestMessage DeserializeRequest(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding);

        return new RequestMessage
        {
            ServiceType = ReadString(reader) ?? throw new InvalidOperationException("ServiceType is required"),
            Method = ReadString(reader) ?? throw new InvalidOperationException("Method is required"),
            Parameters = ReadObjectArray(reader),
            ParameterTypes = ReadStringArray(reader)
        };
    }

    #endregion

    #region ResponseMessage

    public byte[] SerializeResponse(ResponseMessage response)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding))
        {
            // Success
            writer.Write(response.Success);

            // Error
            WriteString(writer, response.Error);

            // ExceptionType
            WriteString(writer, response.ExceptionType);

            // StackTrace
            WriteString(writer, response.StackTrace);

            // Data
            WriteObject(writer, response.Data);

            // DataType
            WriteString(writer, response.DataType);

            // IsVoid
            writer.Write(response.IsVoid);
        }
        return ms.ToArray();
    }

    public ResponseMessage DeserializeResponse(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding);

        return new ResponseMessage
        {
            Success = reader.ReadBoolean(),
            Error = ReadString(reader),
            ExceptionType = ReadString(reader),
            StackTrace = ReadString(reader),
            Data = ReadObject(reader),
            DataType = ReadString(reader),
            IsVoid = reader.ReadBoolean()
        };
    }

    #endregion
    

    #region Private Write Methods

    private void WriteString(BinaryWriter writer, string? value)
    {
        if (value == null)
        {
            writer.Write(-1);
        }
        else
        {
            var bytes = Encoding.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    private void WriteStringArray(BinaryWriter writer, string?[]? array)
    {
        if (array == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(array.Length);
        foreach (var item in array)
        {
            WriteString(writer, item);
        }
    }

    private void WriteObjectArray(BinaryWriter writer, object?[]? array)
    {
        if (array == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(array.Length);
        foreach (var item in array)
        {
            WriteObject(writer, item);
        }
    }

    private void WriteObject(BinaryWriter writer, object? obj)
    {
        if (obj == null)
        {
            writer.Write(TypeNull);
            return;
        }

        var type = obj.GetType();

        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            type = underlyingType;
        }

        switch (obj)
        {
            case string s:
                writer.Write(TypeString);
                WriteString(writer, s);
                break;

            case int i:
                writer.Write(TypeInt32);
                writer.Write(i);
                break;

            case long l:
                writer.Write(TypeInt64);
                writer.Write(l);
                break;

            case double d:
                writer.Write(TypeDouble);
                writer.Write(d);
                break;

            case bool b:
                writer.Write(TypeBool);
                writer.Write(b);
                break;

            case DateTime dt:
                writer.Write(TypeDateTime);
                writer.Write(dt.ToBinary());
                break;

            case byte[] bytes:
                writer.Write(TypeByteArray);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                break;

            default:
                // 复杂对象使用JSON序列化作为后备，同时存储类型信息
                writer.Write(TypeObject);
                WriteString(writer, type.AssemblyQualifiedName); // 存储类型名称
                var jsonBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj, type);
                writer.Write(jsonBytes.Length);
                writer.Write(jsonBytes);
                break;
        }
    }

    #endregion

    #region Private Read Methods

    private string? ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == -1) return null;

        var bytes = reader.ReadBytes(length);
        return Encoding.GetString(bytes);
    }

    private string[]? ReadStringArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == -1) return null;

        var array = new string[length];
        for (int i = 0; i < length; i++)
        {
            array[i] = ReadString(reader) ?? string.Empty;
        }
        return array;
    }

    private object?[]? ReadObjectArray(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == -1) return null;

        var array = new object?[length];
        for (int i = 0; i < length; i++)
        {
            array[i] = ReadObject(reader);
        }
        return array;
    }

    private object? ReadObject(BinaryReader reader)
    {
        var typeMarker = reader.ReadByte();

        switch (typeMarker)
        {
            case TypeNull:
                return null;

            case TypeString:
                return ReadString(reader);

            case TypeInt32:
                return reader.ReadInt32();

            case TypeInt64:
                return reader.ReadInt64();

            case TypeDouble:
                return reader.ReadDouble();

            case TypeBool:
                return reader.ReadBoolean();

            case TypeDateTime:
                return DateTime.FromBinary(reader.ReadInt64());

            case TypeByteArray:
                var byteLength = reader.ReadInt32();
                return reader.ReadBytes(byteLength);

            case TypeObject:
                var typeName = ReadString(reader) ?? string.Empty;
                
                var jsonLength = reader.ReadInt32();
                var jsonBytes = reader.ReadBytes(jsonLength);
                // 复杂对象反序列化为 JsonElement
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonBytes);

                var rawText = jsonElement.GetRawText();
                var type = Type.GetType(typeName);
                if (type == null)
                {
                    throw new InvalidOperationException($"Type not found: {typeName}");
                }

                return JsonSerializer.Deserialize(rawText, type);

            default:
                throw new InvalidOperationException($"Unknown type marker: {typeMarker}");
        }
    }

    #endregion

    #region Auth Messages

    public byte[] SerializeAuthRequest(AuthRequestMessage request)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding))
        {
            // ClientId
            WriteString(writer, request.ClientId);
            // PreviousToken
            WriteString(writer, request.PreviousToken);
            // Credentials count
            if (request.Credentials == null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(request.Credentials.Count);
                foreach (var kvp in request.Credentials)
                {
                    WriteString(writer, kvp.Key);
                    WriteString(writer, kvp.Value);
                }
            }
        }
        return ms.ToArray();
    }

    public AuthRequestMessage DeserializeAuthRequest(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding);

        var clientId = ReadString(reader);
        var previousToken = ReadString(reader);

        Dictionary<string, string>? credentials = null;
        var count = reader.ReadInt32();
        if (count >= 0)
        {
            credentials = new Dictionary<string, string>();
            for (int i = 0; i < count; i++)
            {
                var key = ReadString(reader);
                var value = ReadString(reader);
                if (key != null)
                {
                    credentials[key] = value ?? string.Empty;
                }
            }
        }

        return new AuthRequestMessage
        {
            ClientId = clientId,
            PreviousToken = previousToken,
            Credentials = credentials
        };
    }

    public byte[] SerializeAuthResponse(AuthResponseMessage response)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding))
        {
            // Success
            writer.Write(response.Success);
            // ErrorMessage
            WriteString(writer, response.ErrorMessage);
            // ClientId
            WriteString(writer, response.ClientId);
            // Token
            WriteString(writer, response.Token);
            // ExpiresAt
            writer.Write(response.ExpiresAt.HasValue);
            if (response.ExpiresAt.HasValue)
            {
                writer.Write(response.ExpiresAt.Value.ToBinary());
            }
            // IsSessionRestored
            writer.Write(response.IsSessionRestored);
            // Message
            WriteString(writer, response.Message);
        }
        return ms.ToArray();
    }

    public AuthResponseMessage DeserializeAuthResponse(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding);

        var success = reader.ReadBoolean();
        var errorMessage = ReadString(reader);
        var clientId = ReadString(reader);
        var token = ReadString(reader);

        DateTime? expiresAt = null;
        if (reader.ReadBoolean())
        {
            expiresAt = DateTime.FromBinary(reader.ReadInt64());
        }

        var isSessionRestored = reader.ReadBoolean();
        var message = ReadString(reader);

        return new AuthResponseMessage
        {
            Success = success,
            ErrorMessage = errorMessage,
            ClientId = clientId,
            Token = token,
            ExpiresAt = expiresAt,
            IsSessionRestored = isSessionRestored,
            Message = message
        };
    }

    #endregion
}
