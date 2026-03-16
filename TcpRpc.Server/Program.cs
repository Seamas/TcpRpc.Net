// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Logging;
using TcpRpc.Contract;
using TcpRpc.Formatter;
using TcpRpc.Server;
using TcpRpc.Server.Auth;

// 创建日志工厂
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<RpcContract>();

IMessageSerializer messageSerializer = new BinaryMessageSerializer();

// 配置认证选项
var authOptions = new ServerAuthOptions
{
    RequireAuth = true,  // 启用认证
    TokenLifetime = TimeSpan.FromHours(1),
    SessionTimeout = TimeSpan.FromMinutes(30),
    // 使用简单用户名密码认证
    AuthProvider = new SimpleAuthProvider(new Dictionary<string, string>
    {
        ["admin"] = "admin123",
        ["user"] = "user123",
        ["test"] = "test123"
    }, username => new Dictionary<string, string>
    {
        ["role"] = username == "admin" ? "Administrator" : "User",
        ["loginTime"] = DateTime.UtcNow.ToString("O")
    }),
    TokenGenerator = new SimpleTokenGenerator("TcpRpcServer")
};

// 创建服务器（带认证）
var server = new TcpRpcServer(8888, messageSerializer, loggerFactory, authOptions);

// 使用新的服务注册方式
var instance = new RpcContract(logger);
server.RegisterService<IRpcContract, RpcContract>(instance);

// 添加日志拦截器示例
server.ServiceRegistry.AddInterceptor(new LoggingInterceptor(loggerFactory.CreateLogger<LoggingInterceptor>()));

Console.WriteLine("Starting server on port 8888...");
Console.WriteLine("Authentication: ENABLED");
Console.WriteLine("Valid credentials:");
Console.WriteLine("  - admin / admin123 (Administrator)");
Console.WriteLine("  - user / user123 (User)");
Console.WriteLine("  - test / test123 (User)");
Console.WriteLine();
Console.WriteLine("Supported methods:");
Console.WriteLine("  - Call(RpcRequest) - Legacy");
Console.WriteLine("  - Add(int, int)");
Console.WriteLine("  - Greet(string, string)");
Console.WriteLine("  - LogMessage(string, LogLevel)");
Console.WriteLine("  - ProcessAsync(string, int)");
Console.WriteLine("  - GetUserInfo(int, bool)");
Console.WriteLine("  - ThrowException(string)");
Console.WriteLine();

await server.StartAsync();
Console.WriteLine("Server started. Press Enter to stop...");
Console.ReadLine();

await server.StopAsync();
Console.WriteLine("Server stopped.");

