using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace TcpRpc.Server;

/// <summary>
/// 服务注册表 - 自动扫描和注册服务实现
/// </summary>
public class ServiceRegistry
{
    private readonly ConcurrentDictionary<string, ServiceDescriptor> _services = new();
    private readonly List<IInterceptor> _interceptors = new();
    private readonly ILogger<ServiceRegistry>? _logger;

    public ServiceRegistry(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ServiceRegistry>();
    }

    /// <summary>
    /// 注册服务实现
    /// </summary>
    public void Register<TInterface, TImplementation>(TImplementation instance) where TImplementation : class, TInterface
    {
        var interfaceType = typeof(TInterface);
        var implementationType = typeof(TImplementation);

        Register(interfaceType, implementationType, instance);
    }

    /// <summary>
    /// 注册服务实现
    /// </summary>
    public void Register(Type interfaceType, Type implementationType, object instance)
    {
        var serviceName = interfaceType.FullName!;

        var descriptor = new ServiceDescriptor
        {
            InterfaceType = interfaceType,
            ImplementationType = implementationType,
            Instance = instance,
            Methods = GetMethodMap(interfaceType)
        };

        _services[serviceName] = descriptor;
        _logger?.LogInformation("Registered service: {ServiceName}", serviceName);
    }

    
    /// <summary>
    /// 添加拦截器
    /// </summary>
    public void AddInterceptor(IInterceptor interceptor)
    {
        _interceptors.Add(interceptor);
        _interceptors.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>
    /// 尝试获取服务
    /// </summary>
    public bool TryGetService(string serviceType, out ServiceDescriptor? descriptor)
    {
        return _services.TryGetValue(serviceType, out descriptor);
    }

    /// <summary>
    /// 执行方法调用
    /// </summary>
    public async Task<MethodResult> InvokeAsync(string serviceType, string methodName, object?[]? parameters, string[]? parameterTypes)
    {
        if (!_services.TryGetValue(serviceType, out var descriptor) || descriptor == null)
        {
            return MethodResult.Failure($"Service '{serviceType}' not found");
        }

        if (!descriptor.Methods.TryGetValue(methodName, out var methodInfo))
        {
            return MethodResult.Failure($"Method '{methodName}' not found in service '{serviceType}'");
        }

        try
        {
            // 转换参数类型
            var convertedParams = ConvertParameters(parameters, parameterTypes, methodInfo.GetParameters());

            // 计算实际返回类型（解包 Task<T>）
            var actualReturnType = GetActualReturnType(methodInfo.ReturnType);

            var context = new InvocationContext
            {
                ServiceType = descriptor.InterfaceType,
                Target = descriptor.Instance,
                MethodName = methodName,
                Parameters = convertedParams,
                ParameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray(),
                ReturnType = actualReturnType
            };

            // 构建拦截器链
            var interceptorChain = BuildInterceptorChain(context, async () =>
            {
                var result = methodInfo.Invoke(descriptor.Instance, convertedParams);

                // 处理异步方法
                if (result is Task task)
                {
                    await task;

                    // 获取Task<T>的结果
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                    return null;
                }

                return result;
            });

            var returnValue = await interceptorChain();

            return MethodResult.Ok(returnValue, actualReturnType);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            _logger?.LogError(tie.InnerException, "Error invoking method {MethodName}", methodName);
            return MethodResult.ThrowException(tie.InnerException);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error invoking method {MethodName}", methodName);
            return MethodResult.ThrowException(ex);
        }
    }

    /// <summary>
    /// 获取实际返回类型（解包 Task<T>）
    /// </summary>
    private static Type GetActualReturnType(Type returnType)
    {
        // 处理 Task<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return returnType.GetGenericArguments()[0];
        }
        // 处理 Task（无返回值）
        if (returnType == typeof(Task))
        {
            return typeof(void);
        }
        return returnType;
    }

    private Func<Task<object?>> BuildInterceptorChain(InvocationContext context, Func<Task<object?>> finalAction)
    {
        var chain = finalAction;

        // 反向构建链，保证顺序执行
        for (int i = _interceptors.Count - 1; i >= 0; i--)
        {
            var interceptor = _interceptors[i];
            var next = chain;
            chain = () => interceptor.InvokeAsync(context, next);
        }

        return chain;
    }

    private object?[] ConvertParameters(object?[]? parameters, string[]? parameterTypes, ParameterInfo[] methodParams)
    {
        if (parameters == null || parameters.Length == 0)
            return new object?[methodParams.Length];

        var result = new object?[methodParams.Length];

        for (int i = 0; i < Math.Min(parameters.Length, methodParams.Length); i++)
        {
            var paramValue = parameters[i];
            var targetType = methodParams[i].ParameterType;

            if (paramValue == null)
            {
                result[i] = null;
            }
            else if (paramValue.GetType() == targetType || targetType.IsAssignableFrom(paramValue.GetType()))
            {
                result[i] = paramValue;
            }
            else
            {
                // 尝试类型转换
                try
                {
                    result[i] = Convert.ChangeType(paramValue, targetType);
                }
                catch
                {
                    result[i] = paramValue;
                }
            }
        }

        return result;
    }

    private Dictionary<string, MethodInfo> GetMethodMap(Type interfaceType)
    {
        var methods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var method in interfaceType.GetMethods())
        {
            methods[method.Name] = method;
        }

        return methods;
    }
}

/// <summary>
/// 服务描述符
/// </summary>
public class ServiceDescriptor
{
    public required Type InterfaceType { get; init; }
    public required Type ImplementationType { get; init; }
    public required object Instance { get; init; }
    public required Dictionary<string, MethodInfo> Methods { get; init; }
}

/// <summary>
/// 方法调用结果
/// </summary>
public class MethodResult
{
    public bool Success { get; }
    public object? Value { get; }
    public Type? ValueType { get; }
    public string? Error { get; }
    public Exception? Exception { get; }
    public bool IsVoid { get; }

    private MethodResult(bool success, object? value, Type? valueType, string? error, Exception? exception, bool isVoid)
    {
        Success = success;
        Value = value;
        ValueType = valueType;
        Error = error;
        Exception = exception;
        IsVoid = isVoid;
    }

    public static MethodResult Ok(object? value, Type? valueType)
    {
        var isVoid = valueType == typeof(void) || valueType == null;
        return new MethodResult(true, value, valueType, null, null, isVoid);
    }

    public static MethodResult Failure(string error)
    {
        return new MethodResult(false, null, null, error, null, false);
    }

    public static MethodResult ThrowException(Exception ex)
    {
        return new MethodResult(false, null, null, ex.Message, ex, false);
    }
}
