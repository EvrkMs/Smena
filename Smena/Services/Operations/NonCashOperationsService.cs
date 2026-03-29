using Host.Services.Data;
using Host.Services.Data.Entities;

namespace Host.Services.Operations;

public class NonCashOperationsService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public void AddExpense(int amount, string comment)
    {
        _db.NonCashOperations.Add(new NonCashOperationEntity
        {
            Amount = amount,
            Comment = comment,
            Type = NonCashOperationType.Expense
        });
    }

    public void AddComing(int amount, string comment)
    {
        _db.NonCashOperations.Add(new NonCashOperationEntity
        {
            Amount = amount,
            Comment = comment,
            Type = NonCashOperationType.Coming
        });
    }

    public void AddOperation(int amount, string comment)
    {
        if (amount == 0)
        {
            return;
        }

        var type = amount > 0
            ? NonCashOperationType.Coming
            : NonCashOperationType.Expense;

        _db.NonCashOperations.Add(new NonCashOperationEntity
        {
            Amount = Math.Abs(amount),
            Comment = comment,
            Type = type
        });
    }
}
