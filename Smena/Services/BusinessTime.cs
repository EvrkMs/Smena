namespace Host.Services;

/// <summary>
/// Единая бизнес-таймзона приложения. Источник — таймзона контейнера
/// (переменная окружения TZ, например `TZ=Europe/Moscow` в docker-compose).
///
/// Раньше зона жила в трёх местах и расходилась: TelegramService жёстко ставил
/// UTC+3, панель читала RootPanel:TimeZoneOffsetHours из конфига, а сводка по
/// отчётам резала сутки по UTC-полуночи — одна и та же смена попадала в разные
/// «бизнес-дни» в разных местах.
/// </summary>
public static class BusinessTime
{
    public static TimeZoneInfo Zone => TimeZoneInfo.Local;

    /// <summary>Текущий момент в бизнес-таймзоне.</summary>
    public static DateTimeOffset Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone);

    /// <summary>Локальная (бизнес) дата/время → UTC-момент.</summary>
    public static DateTime ToUtc(DateTime businessLocal)
    {
        var unspecified = DateTime.SpecifyKind(businessLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }
}
