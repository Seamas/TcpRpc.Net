using System.Text.Json;
using TcpRpc.Message;

namespace TcpRpc.Formatter;

public class JsonMessageSerializer: IMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    
    public byte[] SerializeRequest(RequestMessage request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        
        return JsonSerializer.SerializeToUtf8Bytes(request, Options);
    }

    public RequestMessage DeserializeRequest(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));
        
        try
        {
            var request = JsonSerializer.Deserialize<RequestMessage>(data, Options);
                
            if (request == null)
                throw new InvalidOperationException("Deserialized request is null");
                
            // 验证必要字段
            if (request.MessageId <= 0)
                throw new InvalidOperationException($"Invalid MessageId: {request.MessageId}");
                
            if (string.IsNullOrEmpty(request.Method))
                throw new InvalidOperationException("Method cannot be null or empty");
            
            return request;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize request message", ex);
        }
    }

    public byte[] SerializeResponse(ResponseMessage response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        
        return JsonSerializer.SerializeToUtf8Bytes(response, Options);
    }

    public ResponseMessage DeserializeResponse(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
            
        if (data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));

        try
        {
            var response = JsonSerializer.Deserialize<ResponseMessage>(data, Options);
                
            if (response == null)
                throw new InvalidOperationException("Deserialized response is null");
                
            // 验证 MessageId
            if (response.MessageId <= 0)
                throw new InvalidOperationException($"Invalid MessageId: {response.MessageId}");
                
            return response;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize response message", ex);
        }
    }
}