using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Host.Services.RootPanel;

internal sealed class RootPanelLoginRateLimitOptions
{
    public const string SectionName = "RootPanelAuth:LoginRateLimit";

    public int MaxFailedAttempts { get; set; } = 5;
    public int AttemptWindowSeconds { get; set; } = 300;
    public int BlockDurationSeconds { get; set; } = 300;
}

public interface IRootPanelLoginRateLimiter
{
    bool TryGetRetryAfter(string clientKey, out TimeSpan retryAfter);
    void RegisterFailure(string clientKey);
    void RegisterSuccess(string clientKey);
}

internal sealed class InMemoryRootPanelLoginRateLimiter(
    IOptions<RootPanelLoginRateLimitOptions> options) : IRootPanelLoginRateLimiter
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private sealed class AttemptState
    {
        public int FailedCount { get; set; }
        public DateTimeOffset WindowStartedAtUtc { get; set; }
        public DateTimeOffset? BlockedUntilUtc { get; set; }
        public DateTimeOffset LastSeenAtUtc { get; set; }
    }

    private readonly RootPanelLoginRateLimitOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, AttemptState> _states = new(StringComparer.Ordinal);
    private long _lastCleanupUtcTicks;

    public bool TryGetRetryAfter(string clientKey, out TimeSpan retryAfter)
    {
        var key = NormalizeKey(clientKey);
        var now = DateTimeOffset.UtcNow;
        MaybeCleanup(now);

        retryAfter = TimeSpan.Zero;
        if (!_states.TryGetValue(key, out var state))
        {
            return false;
        }

        lock (state)
        {
            state.LastSeenAtUtc = now;

            if (state.BlockedUntilUtc is { } blockedUntil)
            {
                if (blockedUntil > now)
                {
                    retryAfter = blockedUntil - now;
                    return true;
                }

                state.BlockedUntilUtc = null;
                state.FailedCount = 0;
                state.WindowStartedAtUtc = now;
            }

            if (now - state.WindowStartedAtUtc > AttemptWindow)
            {
                state.FailedCount = 0;
                state.WindowStartedAtUtc = now;
            }
        }

        return false;
    }

    public void RegisterFailure(string clientKey)
    {
        var key = NormalizeKey(clientKey);
        var now = DateTimeOffset.UtcNow;
        MaybeCleanup(now);

        var state = _states.GetOrAdd(key, _ => new AttemptState
        {
            WindowStartedAtUtc = now,
            LastSeenAtUtc = now
        });

        lock (state)
        {
            state.LastSeenAtUtc = now;

            if (state.BlockedUntilUtc is { } blockedUntil && blockedUntil > now)
            {
                return;
            }

            if (state.BlockedUntilUtc is { } expiredUntil && expiredUntil <= now)
            {
                state.BlockedUntilUtc = null;
                state.FailedCount = 0;
                state.WindowStartedAtUtc = now;
            }

            if (now - state.WindowStartedAtUtc > AttemptWindow)
            {
                state.FailedCount = 0;
                state.WindowStartedAtUtc = now;
            }

            state.FailedCount++;
            if (state.FailedCount >= MaxFailedAttempts)
            {
                state.BlockedUntilUtc = now + BlockDuration;
                state.FailedCount = 0;
                state.WindowStartedAtUtc = now;
            }
        }
    }

    public void RegisterSuccess(string clientKey)
    {
        var key = NormalizeKey(clientKey);
        _states.TryRemove(key, out _);
    }

    private int MaxFailedAttempts => Math.Max(1, _options.MaxFailedAttempts);

    private TimeSpan AttemptWindow => TimeSpan.FromSeconds(Math.Max(1, _options.AttemptWindowSeconds));

    private TimeSpan BlockDuration => TimeSpan.FromSeconds(Math.Max(1, _options.BlockDurationSeconds));

    private void MaybeCleanup(DateTimeOffset now)
    {
        if (_states.IsEmpty)
        {
            return;
        }

        var nowTicks = now.UtcTicks;
        var previousTicks = Interlocked.Read(ref _lastCleanupUtcTicks);
        if (previousTicks != 0 && nowTicks - previousTicks < CleanupInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastCleanupUtcTicks, nowTicks, previousTicks) != previousTicks)
        {
            return;
        }

        var staleAfter = now - AttemptWindow - BlockDuration;

        foreach (var kv in _states)
        {
            var state = kv.Value;
            var shouldRemove = false;

            lock (state)
            {
                var isBlocked = state.BlockedUntilUtc is { } blockedUntil && blockedUntil > now;
                shouldRemove = !isBlocked && state.LastSeenAtUtc < staleAfter;
            }

            if (shouldRemove)
            {
                _states.TryRemove(kv.Key, out _);
            }
        }
    }

    private static string NormalizeKey(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return "unknown";
        }

        return clientKey.Trim();
    }
}
