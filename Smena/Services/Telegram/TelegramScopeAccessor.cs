using System.Threading;

namespace Host.Services.Telegram;

public interface ITelegramScopeAccessor
{
    TelegramMessageScope? Current { get; set; }
}

internal sealed class TelegramScopeAccessor : ITelegramScopeAccessor
{
    private readonly AsyncLocal<TelegramMessageScope?> _current = new();

    public TelegramMessageScope? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
