using TcpRpc.Message;

namespace TcpRpc.Formatter;

public interface IMessageSerializer
{
    byte[] SerializeRequest(RequestMessage request);
    RequestMessage DeserializeRequest(byte[] data);
    
    
    byte[] SerializeResponse(ResponseMessage response);
    ResponseMessage DeserializeResponse(byte[] data);
}