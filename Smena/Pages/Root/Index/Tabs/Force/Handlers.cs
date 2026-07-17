using Microsoft.AspNetCore.Mvc;

namespace Host.Pages.Root;

public partial class IndexModel
{
    public async Task<IActionResult> OnPostForceAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(Force.EmployeeId, out var employeeId))
        {
            ErrorMessage = "Некорректный сотрудник.";
            return RedirectToPage(new { tab = "force" });
        }

        if (Force.Amount <= 0)
        {
            ErrorMessage = "Сумма должна быть больше 0.";
            return RedirectToPage(new { tab = "force" });
        }

        var comment = string.IsNullOrWhiteSpace(Force.Comment)
            ? (Force.IsSalary ? "ROOT: принудительная выплата зарплаты" : "ROOT: принудительный аванс")
            : $"ROOT: {Force.Comment}";

        // Вся денежная логика — в AdvanceOperationsService (раньше handler
        // дублировал её и расходился с gRPC-потоком). skipBalanceCheck: true —
        // root-выплата сознательно может увести ЗП в минус. Публикацию сейфа
        // после коммита сервис делает сам.
        var scope = _telegramService.CreateScope();
        try
        {
            var result = await _advanceOperationsService.SendAdvanceAsync(
                employeeId,
                Force.Amount,
                Force.IsSalary,
                comment,
                extractFromSafe: !Force.IsNonCash,
                isNonCash: Force.IsNonCash,
                scope,
                ct,
                skipBalanceCheck: true);

            if (result.Success)
            {
                StatusMessage = "Принудительная выплата применена.";
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            await scope.RollbackAsync(CancellationToken.None);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { tab = "force" });
    }
}
