namespace Host.Services.Data;

public static class TransactionHelper
{
    public static async Task ExecuteAsync(AppDbContext db, Func<Task> body, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            await body();
            await tx.CommitAsync(ct);
            committed = true;
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
