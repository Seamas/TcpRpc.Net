using Microsoft.Extensions.Logging;

namespace TcpRpc.Server;

/// <summary>
/// 日志拦截器示例
/// </summary>
public class LoggingInterceptor : IInterceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;

    public int Order => 0;

    public LoggingInterceptor(ILogger<LoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public async Task<object?> InvokeAsync(InvocationContext context, Func<Task<object?>> next)
    {
        _logger.LogInformation("[Intercept] Calling {MethodName} on {ServiceType}",
            context.MethodName, context.ServiceType.Name);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await next();
            stopwatch.Stop();
            _logger.LogInformation("[Intercept] {MethodName} completed in {ElapsedMs}ms",
                context.MethodName, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[Intercept] {MethodName} failed after {ElapsedMs}ms",
                context.MethodName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}