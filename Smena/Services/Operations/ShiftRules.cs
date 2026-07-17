namespace Host.Services.Operations;

/// <summary>
/// Единственный источник бизнес-констант смены. Отдаются клиенту через
/// GrpcConstantsService — раньше клиент зеркалил эти значения в своём
/// ShiftConstants.cs, и изменение (например, начальной кассы) требовало
/// синхронного редеплоя сервера и клиента, иначе расходилась математика сейфа.
/// </summary>
public static class ShiftRules
{
    /// <summary>Начальная сумма наличных в кассе на начало смены.</summary>
    public const int InitialCashRegister = 1000;

    /// <summary>Максимальное количество сотрудников в смене.</summary>
    public const int MaxEmployeesPerShift = 3;

    /// <summary>Максимальное суммарное количество часов в смене.</summary>
    public const int MaxHoursPerShift = 12;

    /// <summary>Лимит цифр при вводе сумм на клиенте.</summary>
    public const int MaxAmountDigits = 6;

    /// <summary>Лимит цифр при вводе часов на клиенте.</summary>
    public const int MaxHoursDigits = 2;
}
