using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TcpRpc.Server.Auth;

/// <summary>
/// 客户端会话管理器
/// </summary>
public class ClientSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _tokenToSessionMap = new();
    private readonly ILogger<ClientSessionManager> _logger;
    private readonly Timer? _cleanupTimer;
    private readonly TimeSpan _sessionTimeout;

    public ClientSessionManager(ILoggerFactory loggerFactory, TimeSpan? sessionTimeout = null, TimeSpan? cleanupInterval = null)
    {
        _logger = loggerFactory.CreateLogger<ClientSessionManager>();
        _sessionTimeout = sessionTimeout ?? TimeSpan.FromHours(1);

        // 启动定时清理任务
        var interval = cleanupInterval ?? TimeSpan.FromMinutes(5);
        _cleanupTimer = new Timer(CleanupExpiredSessions, null, interval, interval);
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    public ClientSession CreateSession(string clientId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new ClientSession(sessionId, clientId);

        if (_sessions.TryAdd(sessionId, session))
        {
            _logger.LogDebug("Created session {SessionId} for client {ClientId}", sessionId, clientId);
            return session;
        }

        throw new InvalidOperationException("Failed to create session");
    }

    /// <summary>
    /// 获取会话
    /// </summary>
    public ClientSession? GetSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Touch();
            return session;
        }
        return null;
    }

    /// <summary>
    /// 通过 Token 查找会话
    /// </summary>
    public ClientSession? GetSessionByToken(string token)
    {
        if (_tokenToSessionMap.TryGetValue(token, out var sessionId))
        {
            return GetSession(sessionId);
        }
        return null;
    }

    /// <summary>
    /// 更新会话的 Token 映射
    /// </summary>
    public void UpdateTokenMapping(string token, string sessionId)
    {
        // 移除旧的 Token 映射
        var session = GetSession(sessionId);
        if (session?.CurrentToken != null)
        {
            _tokenToSessionMap.TryRemove(session.CurrentToken, out _);
        }

        // 添加新的 Token 映射
        _tokenToSessionMap[token] = sessionId;
    }

    /// <summary>
    /// 尝试恢复会话（用于断线重连）
    /// </summary>
    public ClientSession? TryRestoreSession(string clientId, string previousToken)
    {
        // 通过 Token 查找原会话
        var session = GetSessionByToken(previousToken);
        if (session == null)
        {
            _logger.LogWarning("Session not found for token, cannot restore");
            return null;
        }

        // 验证 ClientId 是否匹配
        if (session.ClientId != clientId)
        {
            _logger.LogWarning("ClientId mismatch during session restore. Expected: {Expected}, Actual: {Actual}",
                session.ClientId, clientId);
            return null;
        }

        // 检查会话是否过期
        if (IsSessionExpired(session))
        {
            _logger.LogWarning("Session {SessionId} has expired, cannot restore", session.SessionId);
            return null;
        }

        _logger.LogInformation("Restored session {SessionId} for client {ClientId}", session.SessionId, clientId);
        session.Touch();
        return session;
    }

    /// <summary>
    /// 移除会话
    /// </summary>
    public bool RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            if (session.CurrentToken != null)
            {
                _tokenToSessionMap.TryRemove(session.CurrentToken, out _);
            }
            _logger.LogDebug("Removed session {SessionId}", sessionId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查会话是否过期
    /// </summary>
    public bool IsSessionExpired(ClientSession session)
    {
        return DateTime.UtcNow - session.LastActivityAt > _sessionTimeout;
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    public IEnumerable<ClientSession> GetAllSessions()
    {
        return _sessions.Values.ToList();
    }

    /// <summary>
    /// 获取活跃会话数
    /// </summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>
    /// 清理过期会话
    /// </summary>
    private void CleanupExpiredSessions(object? state)
    {
        try
        {
            var expiredSessions = _sessions.Values.Where(IsSessionExpired).ToList();
            foreach (var session in expiredSessions)
            {
                RemoveSession(session.SessionId);
                _logger.LogDebug("Cleaned up expired session {SessionId}", session.SessionId);
            }

            if (expiredSessions.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _sessions.Clear();
        _tokenToSessionMap.Clear();
    }
}
