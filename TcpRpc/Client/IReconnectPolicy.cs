namespace TcpRpc.Client;

/// <summary>
/// 重连策略接口
/// </summary>
public interface IReconnectPolicy
{
    /// <summary>
    /// 最大重连次数
    /// </summary>
    int MaxReconnectAttempts { get; }

    /// <summary>
    /// 获取下次重连前的延迟时间
    /// </summary>
    /// <param name="attemptNumber">当前尝试次数（从1开始）</param>
    /// <returns>延迟时间</returns>
    TimeSpan GetDelay(int attemptNumber);

    /// <summary>
    /// 是否可以继续重连
    /// </summary>
    /// <param name="attemptNumber">当前尝试次数</param>
    /// <returns>是否可以继续</returns>
    bool CanReconnect(int attemptNumber);

    /// <summary>
    /// 重置策略状态
    /// </summary>
    void Reset();
}

/// <summary>
/// 固定延迟重连策略
/// </summary>
public class FixedDelayReconnectPolicy : IReconnectPolicy
{
    public int MaxReconnectAttempts { get; }
    public TimeSpan Delay { get; }

    public FixedDelayReconnectPolicy(int maxAttempts = 3, TimeSpan? delay = null)
    {
        MaxReconnectAttempts = maxAttempts;
        Delay = delay ?? TimeSpan.FromSeconds(1);
    }

    public TimeSpan GetDelay(int attemptNumber) => Delay;

    public bool CanReconnect(int attemptNumber) => attemptNumber <= MaxReconnectAttempts;

    public void Reset() { }
}

/// <summary>
/// 指数退避重连策略
/// </summary>
public class ExponentialBackoffReconnectPolicy : IReconnectPolicy
{
    public int MaxReconnectAttempts { get; }
    public TimeSpan InitialDelay { get; }
    public TimeSpan MaxDelay { get; }
    public double BackoffMultiplier { get; }

    public ExponentialBackoffReconnectPolicy(
        int maxAttempts = 5,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0)
    {
        MaxReconnectAttempts = maxAttempts;
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
        MaxDelay = maxDelay ?? TimeSpan.FromMinutes(1);
        BackoffMultiplier = backoffMultiplier;
    }

    public TimeSpan GetDelay(int attemptNumber)
    {
        var delay = TimeSpan.FromTicks((long)(InitialDelay.Ticks * Math.Pow(BackoffMultiplier, attemptNumber - 1)));
        return delay > MaxDelay ? MaxDelay : delay;
    }

    public bool CanReconnect(int attemptNumber) => attemptNumber <= MaxReconnectAttempts;

    public void Reset() { }
}

/// <summary>
/// 不重连策略
/// </summary>
public class NoReconnectPolicy : IReconnectPolicy
{
    public int MaxReconnectAttempts => 0;

    public TimeSpan GetDelay(int attemptNumber) => TimeSpan.Zero;

    public bool CanReconnect(int attemptNumber) => false;

    public void Reset() { }
}
