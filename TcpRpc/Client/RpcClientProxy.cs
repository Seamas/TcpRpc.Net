using System.Reflection;
using Microsoft.Extensions.Logging;
using TcpRpc.Formatter;
using TcpRpc.Message;

namespace TcpRpc.Client;

/// <summary>
/// RPC客户端代理 - 支持多参数、Void返回和异常传播
/// </summary>
public class RpcClientProxy : DispatchProxy
{
    private TcpRpcClient _client;
    private Type _interfaceType;
    private ILoggerFactory _factory;
    private ILogger<RpcClientProxy> _logger;
    private IMessageSerializer _serializer;
    
    /// <summary>
    /// 创建代理实例
    /// </summary>
    public static T Create<T>(TcpRpcClient client, IMessageSerializer _serializer, ILoggerFactory factory) where T : class
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        var proxy = Create<T, RpcClientProxy>();
        var rpcProxy = (RpcClientProxy)(object)proxy;
        rpcProxy._client = client;
        rpcProxy._interfaceType = typeof(T);
        rpcProxy._serializer = _serializer;
        rpcProxy._factory = factory;
        rpcProxy._logger = rpcProxy._factory.CreateLogger<RpcClientProxy>();

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (_client == null)
            throw new InvalidOperationException("Client not initialized");

        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        if (_interfaceType == null)
            throw new InvalidOperationException("Interface type not set");

        var methodName = targetMethod.Name;
        var serviceType = _interfaceType.FullName!;
        var returnType = targetMethod.ReturnType;

        _logger.LogDebug("Invoking {ServiceType}.{MethodName}", serviceType, methodName);

        // 获取参数类型信息
        var parameters = targetMethod.GetParameters();
        var parameterTypes = parameters.Select(p => p.ParameterType.FullName!).ToArray();

        // 构建请求
        var request = new RequestMessage
        {
            ServiceType = serviceType,
            Method = methodName,
            Parameters = args,
            ParameterTypes = parameterTypes
        };

        try
        {
            // 发送请求
            var response = SendRequestAsync(request).GetAwaiter().GetResult();

            // 处理异常
            if (!response.Success)
            {
                if (!string.IsNullOrEmpty(response.ExceptionType))
                {
                    throw new RpcException(
                        response.Error ?? "Remote exception",
                        response.ExceptionType,
                        response.StackTrace);
                }
                throw new InvalidOperationException(response.Error ?? "Request failed");
            }

            // 处理Void返回
            if (response.IsVoid && returnType == typeof(void))
            {
                return null;
            }

            if (response.IsVoid && returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            // 处理Task<T>返回
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var result = ConvertResponseData(response.Data, resultType);
                return CreateTaskOfType(result, resultType);
            }

            // 同步返回
            return ConvertResponseData(response.Data, returnType);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            _logger.LogError(ex, "Error invoking {ServiceType}.{MethodName}", serviceType, methodName);
            throw;
        }
    }

    private async Task<ResponseMessage> SendRequestAsync(RequestMessage request)
    {
        // 序列化请求
        var requestData = _serializer.SerializeRequest(request);

        // 使用客户端发送请求
        var response = await _client!.SendRequestAsync(
            $"{request.ServiceType}.{request.Method}",
            requestData);

        return response;
    }

    private object? ConvertResponseData(object? data, Type targetType)
    {
        if (data == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType.IsInstanceOfType(data))
            return data;

        // 尝试转换
        try
        {
            return Convert.ChangeType(data, targetType);
        }
        catch
        {
            return data;
        }
    }

    /// <summary>
    /// 创建特定类型的 Task<T>
    /// </summary>
    private static object CreateTaskOfType(object? result, Type resultType)
    {
        // 使用反射调用 Task.FromResult<T>(result)
        var fromResultMethod = typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType);
        return fromResultMethod.Invoke(null, [result])!;
    }
}


