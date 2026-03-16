using TcpRpc.Message;

namespace TcpRpc.Formatter;

/// <summary>
/// 消息序列化器接口 - 支持对象序列化
/// </summary>
public interface IMessageSerializer
{
    byte[] SerializeRequest(RequestMessage request);
    RequestMessage DeserializeRequest(byte[] data);

    byte[] SerializeResponse(ResponseMessage response);
    ResponseMessage DeserializeResponse(byte[] data);
    
}