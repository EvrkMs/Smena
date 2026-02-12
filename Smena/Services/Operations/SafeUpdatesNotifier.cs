using System.Threading.Channels;

namespace Host.Services.Operations;

/// <summary>
/// In-memory broadcaster for safe balance updates.
/// Single server instance, single client expected, but supports multiple subscribers.
/// </summary>
public class SafeUpdatesNotifier
{
    private readonly object _lock = new();
    private readonly List<Channel<long>> _channels = [];

    public Channel<long> Subscribe()
    {
        var channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_lock)
        {
            _channels.Add(channel);
        }

        return channel;
    }

    public void Unsubscribe(Channel<long> channel)
    {
        lock (_lock)
        {
            _channels.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    public void Publish(long newValue)
    {
        Channel<long>[] snapshot;

        lock (_lock)
        {
            snapshot = _channels.ToArray();
        }

        foreach (var ch in snapshot)
        {
            ch.Writer.TryWrite(newValue);
        }
    }
}
