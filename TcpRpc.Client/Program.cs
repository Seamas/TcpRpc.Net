// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Logging;
using TcpRpc;
using TcpRpc.Client;
using TcpRpc.Contract;
using TcpRpc.Formatter;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
});

Console.WriteLine("=== TcpRpc Client Demo ===\n");

IMessageSerializer serializer = new BinaryMessageSerializer();

// 方式1: 使用工厂创建代理（推荐）
using var factory = new RpcClientFactory("127.0.0.1", 8888, serializer, loggerFactory, usePool: true, poolSize: 5);

var contract = await factory.CreateProxyAsync<IRpcContract>();

// 1. 旧版兼容测试
Console.WriteLine("1. Legacy Call Test:");
var request = new RpcRequest { Message = "Hello, Server!" };
var response = contract.Call(request);
Console.WriteLine($"   Response: {response.Message}\n");

// 2. 多参数调用测试
Console.WriteLine("2. Multi-parameter Test:");
var sum = contract.Add(10, 20);
Console.WriteLine($"   Add(10, 20) = {sum}\n");

// 3. 字符串处理测试
Console.WriteLine("3. String Processing Test:");
var greeting = contract.Greet("World", "Hello");
Console.WriteLine($"   Greet(\"World\", \"Hello\") = {greeting}\n");

// 4. Void返回测试
Console.WriteLine("4. Void Return Test:");
contract.LogMessage("This is a test log", TcpRpc.Contract.LogLevel.Info);
Console.WriteLine("   LogMessage called successfully\n");

// 5. 异步方法测试
Console.WriteLine("5. Async Method Test:");
var processed = await contract.ProcessAsync("hello world", 100);

await contract.ProcessAsyncWithoutReturn("hello world", 100);
Console.WriteLine($"   ProcessAsync result: {processed}\n");

// 6. 复杂对象测试
Console.WriteLine("6. Complex Object Test:");
var userInfo = contract.GetUserInfo(42, true);
Console.WriteLine($"   User: {userInfo.Name} ({userInfo.Email})");
Console.WriteLine($"   Created: {userInfo.CreatedAt:yyyy-MM-dd}");
if (userInfo.Details != null)
{
    Console.WriteLine($"   Department: {userInfo.Details["Department"]}");
}
Console.WriteLine();

// 7. 异常传播测试
Console.WriteLine("7. Exception Propagation Test:");
try
{
    contract.ThrowException("Something went wrong!");
}
catch (RpcException ex)
{
    Console.WriteLine($"   Caught RpcException: {ex.Message}");
    Console.WriteLine($"   Remote Type: {ex.RemoteExceptionType}\n");
}
catch (Exception ex)
{
    Console.WriteLine($"   Caught Exception: {ex.GetType()}");
}

// 方式2: 使用连接池直接获取客户端
Console.WriteLine("8. Connection Pool Test:");
var pool = new ConnectionPool("127.0.0.1", 8888, serializer, loggerFactory, maxSize: 3);

// 并行测试连接池
var tasks = Enumerable.Range(0, 5).Select(async i =>
{
    
    var pooledClient = await pool.AcquireAsync();
    try
    {
        var proxy = RpcClientProxy.Create<IRpcContract>(pooledClient, serializer, loggerFactory);
        var result = proxy.Add(i, i * 10);
        Console.WriteLine($"   Task {i}: Add({i}, {i * 10}) = {result}");
    }
    finally
    {
        pool.Release(pooledClient);
    }
});

await Task.WhenAll(tasks);

pool.Dispose();

Console.WriteLine("\n=== All tests completed ===");
Console.ReadLine();