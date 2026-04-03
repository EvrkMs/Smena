using System.Collections.Concurrent;

namespace Host.Services.Photo;

public class PhotoSessionStore
{
    private sealed record PhotoSession(
        IReadOnlyList<string> FileIds,
        DateTime CreatedAtUtc);

    private readonly ConcurrentDictionary<string, PhotoSession> _sessions = new();

    public string CreateSession(IReadOnlyList<string> fileIds)
    {
        var key = Guid.NewGuid().ToString("N");
        _sessions[key] = new PhotoSession(fileIds, DateTime.UtcNow);
        return key;
    }

    public bool TryGetSession(string key, TimeSpan ttl, out IReadOnlyList<string> fileIds)
    {
        fileIds = Array.Empty<string>();
        if (!_sessions.TryGetValue(key, out var session))
        {
            return false;
        }

        if (DateTime.UtcNow - session.CreatedAtUtc > ttl)
        {
            _sessions.TryRemove(key, out _);
            return false;
        }

        fileIds = session.FileIds;
        return true;
    }

    public void RemoveSession(string key)
    {
        _sessions.TryRemove(key, out _);
    }

    public int CleanupExpired(TimeSpan ttl)
    {
        var removed = 0;
        var cutoff = DateTime.UtcNow - ttl;
        foreach (var key in _sessions.Keys)
        {
            if (_sessions.TryGetValue(key, out var session) && session.CreatedAtUtc < cutoff)
            {
                if (_sessions.TryRemove(key, out _))
                    removed++;
            }
        }
        return removed;
    }
}
