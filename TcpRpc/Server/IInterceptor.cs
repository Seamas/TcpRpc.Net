namespace TcpRpc.Server;

/// <summary>
/// 拦截器接口 - 支持AOP编程
/// </summary>
public interface IInterceptor
{
    /// <summary>
    /// 拦截顺序，数值越小优先级越高
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 拦截方法调用
    /// </summary>
    Task<object?> InvokeAsync(InvocationContext context, Func<Task<object?>> next);
}

/// <summary>
/// 方法调用上下文
/// </summary>
public class InvocationContext
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public required Type ServiceType { get; init; }

    /// <summary>
    /// 实现实例
    /// </summary>
    public required object Target { get; init; }

    /// <summary>
    /// 方法名
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// 方法参数
    /// </summary>
    public required object?[] Parameters { get; init; }

    /// <summary>
    /// 参数类型
    /// </summary>
    public required Type[] ParameterTypes { get; init; }

    /// <summary>
    /// 返回类型
    /// </summary>
    public required Type ReturnType { get; init; }

    /// <summary>
    /// 是否为Void返回
    /// </summary>
    public bool IsVoidReturn => ReturnType == typeof(void) || ReturnType == typeof(Task);

    /// <summary>
    /// 用户自定义数据
    /// </summary>
    public Dictionary<string, object> Items { get; } = new();
}
