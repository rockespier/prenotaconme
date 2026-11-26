using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Finance.Domain;

public enum ExpenseKind
{
    /// <summary>Repeats every month from <see cref="Expense.Date"/> to <see cref="Expense.EndsOn"/> (rent, utilities).</summary>
    Fixed,

    /// <summary>Happens once on <see cref="Expense.Date"/> (products, commissions).</summary>
    Variable,
}

/// <summary>F4-02: a business expense, for one location or the whole business (<see cref="LocationId"/> null).</summary>
public sealed class Expense : TenantEntity
{
    public const int NameMaxLength = 100;
    public const decimal MaxAmount = 1_000_000m;

    private Expense()
    {
    }

    public ExpenseKind Kind { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public Guid? LocationId { get; private set; }

    /// <summary>Variable: the day it was paid. Fixed: first month it applies (always the 1st).</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Fixed only: last month it applies (the 1st), or null while it lasts.</summary>
    public DateOnly? EndsOn { get; private set; }

    public static Expense Create(ExpenseKind kind, string name, decimal amount, Guid? locationId, DateOnly date, DateOnly? endsOn)
    {
        var expense = new Expense();
        expense.Update(kind, name, amount, locationId, date, endsOn);
        return expense;
    }

    /// <summary>Inputs must already be validated; fixed expenses are normalized to whole months.</summary>
    public void Update(ExpenseKind kind, string name, decimal amount, Guid? locationId, DateOnly date, DateOnly? endsOn)
    {
        Kind = kind;
        Name = name.Trim();
        Amount = amount;
        LocationId = locationId;
        Date = kind == ExpenseKind.Fixed ? FirstOfMonth(date) : date;
        EndsOn = kind == ExpenseKind.Fixed && endsOn is { } end ? FirstOfMonth(end) : null;
    }

    /// <summary>Amount this expense adds to the month starting on <paramref name="month"/> (a 1st).</summary>
    public decimal AmountIn(DateOnly month) => Kind switch
    {
        ExpenseKind.Fixed when Date <= month && (EndsOn is null || EndsOn >= month) => Amount,
        ExpenseKind.Variable when FirstOfMonth(Date) == month => Amount,
        _ => 0m,
    };

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
