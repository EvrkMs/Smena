namespace Host.Services.Data;

public static class TransactionHelper
{
    public static Task ExecuteAsync(AppDbContext db, Func<Task> body, CancellationToken ct)
        => ExecuteAsync(db, async () => { await body(); return true; }, ct);

    /// <summary>
    /// Вариант с результатом: позволяет выполнять валидации, зависящие от
    /// балансов, ПОД транзакцией и advisory-блокировками и возвращать
    /// OperationResult.Fail без исключений (транзакция при этом коммитится
    /// пустой — ничего не добавлено, это безопасно).
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        AppDbContext db, Func<Task<TResult>> body, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var result = await body();
            await tx.CommitAsync(ct);
            committed = true;
            return result;
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await tx.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // swallow rollback failure; interceptor handles user-facing error
                }
            }
        }
    }
}
