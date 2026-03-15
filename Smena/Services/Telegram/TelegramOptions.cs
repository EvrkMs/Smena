namespace Host.Services.Telegram;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string Token { get; set; } = string.Empty;
    public int ConnectTimeoutSeconds { get; set; } = 180;
    public int HttpTimeoutSeconds { get; set; } = 180;
    public long ForwardChatId { get; set; }
    public long SalaryChatId { get; set; }
    public int RaportThreadId { get; set; }
    public int ExpensesThreadId { get; set; }
    public int SafeThreadId { get; set; }
}
