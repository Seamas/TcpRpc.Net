using Microsoft.Extensions.Logging;
using TcpRpc.Contract;
using LogLevel = TcpRpc.Contract.LogLevel;

namespace TcpRpc.Server;

/// <summary>
/// RPC服务实现 - 展示多种方法签名
/// </summary>
public class RpcContract : IRpcContract
{
    private readonly ILogger<RpcContract> _logger;

    public RpcContract(ILogger<RpcContract> logger)
    {
        _logger = logger;
    }

    public RpcResponse Call(RpcRequest request)
    {
        _logger.LogInformation("Received request: {RequestMessage}", request.Message);
        return new RpcResponse { Message = "Hello, World!" };
    }

    public int Add(int a, int b)
    {
        _logger.LogInformation("Add called: {A} + {B}", a, b);
        return a + b;
    }

    public string Greet(string name, string greeting)
    {
        _logger.LogInformation("Greet called: {Greeting} {Name}", greeting, name);
        return $"{greeting}, {name}!";
    }
    

    public void LogMessage(string message, LogLevel level)
    {
        _logger.Log((Microsoft.Extensions.Logging.LogLevel)level, "Client log: {Message}", message);
    }

    public async Task<string> ProcessAsync(string input, int delayMs)
    {
        _logger.LogInformation("ProcessAsync called with delay: {DelayMs}ms", delayMs);
        await Task.Delay(delayMs);
        return $"Processed: {input.ToUpper()}"; 
    }
    
    public async Task ProcessAsyncWithoutReturn(string input, int delayMs)
    {
        _logger.LogInformation("ProcessAsyncWithoutReturn called with delay: {DelayMs}ms", delayMs);
        await Task.Delay(delayMs);
        _logger.LogInformation("ProcessAsyncWithoutReturn completed: {Input}", input);
    }

    public UserInfo GetUserInfo(int userId, bool includeDetails)
    {
        _logger.LogInformation("GetUserInfo called: UserId={UserId}, IncludeDetails={IncludeDetails}", userId, includeDetails);

        var user = new UserInfo
        {
            Id = userId,
            Name = $"User_{userId}",
            Email = $"user{userId}@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-userId),
            Details = includeDetails ? new Dictionary<string, object>
            {
                ["Department"] = "Engineering",
                ["Level"] = "Senior",
                ["Projects"] = new[] { "Project A", "Project B" }
            } : null
        };

        return user;
    }

    public void ThrowException(string message)
    {
        _logger.LogWarning("ThrowException called: {Message}", message);
        throw new InvalidOperationException($"Server exception: {message}");
    }
}