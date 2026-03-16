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

    /// <summary>
    /// 序列化认证请求消息
    /// </summary>
    byte[] SerializeAuthRequest(AuthRequestMessage request);

    /// <summary>
    /// 反序列化认证请求消息
    /// </summary>
    AuthRequestMessage DeserializeAuthRequest(byte[] data);

    /// <summary>
    /// 序列化认证响应消息
    /// </summary>
    byte[] SerializeAuthResponse(AuthResponseMessage response);

    /// <summary>
    /// 反序列化认证响应消息
    /// </summary>
    AuthResponseMessage DeserializeAuthResponse(byte[] data);
}