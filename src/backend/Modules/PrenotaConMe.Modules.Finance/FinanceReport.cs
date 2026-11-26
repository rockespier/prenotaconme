using PrenotaConMe.Modules.Finance.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;

namespace PrenotaConMe.Modules.Finance;

public sealed record ExpenseDto(Guid Id, ExpenseKind Kind, string Name, decimal Amount, Guid? LocationId, DateOnly Date, DateOnly? EndsOn);

/// <param name="Date">Variable: the day. Fixed: first month (any day of it).</param>
/// <param name="EndsOn">Fixed only: last month, or null while it lasts.</param>
public sealed record SaveExpenseRequest(ExpenseKind Kind, string Name, decimal Amount, Guid? LocationId, DateOnly Date, DateOnly? EndsOn);

/// <param name="Appointments">Done appointments: completed, or past and never marked.</param>
public sealed record FinanceTotalsDto(
    decimal Income, decimal FixedExpenses, decimal VariableExpenses, decimal Net, int Appointments, int NoShows);

public sealed record FinanceServiceLineDto(string ServiceName, int Count, decimal Sales);

public sealed record FinanceStaffDto(
    Guid StaffMemberId, string Name, int Appointments, int NoShows, decimal Sales, IReadOnlyList<FinanceServiceLineDto> Services);

public sealed record FinanceDayDto(DateOnly Date, decimal Income);

/// <param name="Month">yyyy-MM, in the business's time zone.</param>
/// <param name="ExpectedIncome">Confirmed appointments still to come this month, at list price.</param>
/// <param name="Unmarked">Past appointments nobody marked as completed or no-show (counted at list price).</param>
public sealed record FinanceReportDto(
    string Month, Guid? LocationId, FinanceTotalsDto Totals, FinanceTotalsDto Previous, decimal ExpectedIncome, int Unmarked,
    IReadOnlyList<FinanceDayDto> Days, IReadOnlyList<FinanceStaffDto> Staff, IReadOnlyList<FinanceServiceLineDto> Services,
    IReadOnlyList<ExpenseDto> Expenses);

/// <summary>One appointment as the report needs it (no I/O in <see cref="FinanceCalculator"/>).</summary>
public sealed record FinanceAppointment(
    Guid StaffMemberId, string ServiceName, decimal Price, decimal? ChargedAmount, AppointmentStatus Status,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// F4-03/04/05: the monthly report as a pure function of appointments and expenses, so it is unit-tested directly.
/// Income: completed at the amount charged, past appointments nobody marked at the list price (a busy owner who
/// never marks anything still gets sensible numbers), no-shows and cancellations at zero.
/// </summary>
public static class FinanceCalculator
{
    public static FinanceTotalsDto Totals(IReadOnlyList<FinanceAppointment> appointments, IReadOnlyList<Expense> expenses, DateOnly month, DateTimeOffset now)
    {
        var income = appointments.Sum(a => Income(a, now));
        var fixedExpenses = expenses.Where(e => e.Kind == ExpenseKind.Fixed).Sum(e => e.AmountIn(month));
        var variableExpenses = expenses.Where(e => e.Kind == ExpenseKind.Variable).Sum(e => e.AmountIn(month));
        return new FinanceTotalsDto(
            income, fixedExpenses, variableExpenses, income - fixedExpenses - variableExpenses,
            appointments.Count(a => IsDone(a, now)), appointments.Count(a => a.Status == AppointmentStatus.NoShow));
    }

    public static FinanceReportDto Build(
        DateOnly month, Guid? locationId, TimeZoneInfo timeZone, DateTimeOffset now,
        IReadOnlyList<FinanceAppointment> appointments, IReadOnlyList<FinanceAppointment> previousAppointments,
        IReadOnlyList<Expense> expenses, IReadOnlyDictionary<Guid, string> staffNames)
    {
        var previousMonth = month.AddMonths(-1);
        var days = Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month))
            .Select(d => month.AddDays(d))
            .Select(day => new FinanceDayDto(day, appointments
                .Where(a => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(a.StartsAt, timeZone).DateTime) == day)
                .Sum(a => Income(a, now))))
            .ToList();

        var staff = appointments
            .GroupBy(a => a.StaffMemberId)
            .Select(g => new FinanceStaffDto(
                g.Key,
                staffNames.GetValueOrDefault(g.Key) ?? "?",
                g.Count(a => IsDone(a, now)),
                g.Count(a => a.Status == AppointmentStatus.NoShow),
                g.Sum(a => Income(a, now)),
                Services(g, now)))
            .Where(s => s.Appointments > 0 || s.NoShows > 0)
            .OrderByDescending(s => s.Sales)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();

        return new FinanceReportDto(
            $"{month:yyyy-MM}",
            locationId,
            Totals(appointments, expenses, month, now),
            Totals(previousAppointments, expenses, previousMonth, now),
            appointments.Where(a => a.Status == AppointmentStatus.Confirmed && a.EndsAt > now).Sum(a => a.Price),
            appointments.Count(a => a.Status == AppointmentStatus.Confirmed && a.EndsAt <= now),
            days,
            staff,
            Services(appointments, now),
            expenses.Where(e => e.AmountIn(month) > 0)
                .OrderBy(e => e.Kind).ThenBy(e => e.Date).ThenBy(e => e.Name, StringComparer.CurrentCulture)
                .Select(ToDto)
                .ToList());
    }

    public static ExpenseDto ToDto(Expense e) => new(e.Id, e.Kind, e.Name, e.Amount, e.LocationId, e.Date, e.EndsOn);

    private static List<FinanceServiceLineDto> Services(IEnumerable<FinanceAppointment> appointments, DateTimeOffset now) =>
        appointments
            .Where(a => IsDone(a, now))
            .GroupBy(a => a.ServiceName)
            .Select(g => new FinanceServiceLineDto(g.Key, g.Count(), g.Sum(a => Income(a, now))))
            .OrderByDescending(s => s.Sales)
            .ThenBy(s => s.ServiceName, StringComparer.CurrentCulture)
            .ToList();

    private static bool IsDone(FinanceAppointment a, DateTimeOffset now) =>
        a.Status == AppointmentStatus.Completed || (a.Status == AppointmentStatus.Confirmed && a.EndsAt <= now);

    private static decimal Income(FinanceAppointment a, DateTimeOffset now) => a.Status switch
    {
        AppointmentStatus.Completed => a.ChargedAmount ?? a.Price,
        AppointmentStatus.Confirmed when a.EndsAt <= now => a.Price,
        _ => 0m,
    };
}
