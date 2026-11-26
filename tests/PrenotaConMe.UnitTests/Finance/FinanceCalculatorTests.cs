using PrenotaConMe.Modules.Finance;
using PrenotaConMe.Modules.Finance.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;

namespace PrenotaConMe.UnitTests.Finance;

public sealed class FinanceCalculatorTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Mario = Guid.NewGuid();
    private static readonly Guid Luigi = Guid.NewGuid();

    private static FinanceAppointment At(Guid staff, string service, decimal price, AppointmentStatus status, int day, decimal? charged = null)
    {
        var start = new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.Zero);
        return new FinanceAppointment(staff, service, price, charged, status, start, start.AddMinutes(30));
    }

    [Fact]
    public void Income_counts_charged_amounts_and_unmarked_past_appointments_but_not_no_shows_or_future_ones()
    {
        var appointments = new[]
        {
            At(Mario, "Taglio", 20m, AppointmentStatus.Completed, 3, charged: 25m),
            At(Mario, "Taglio", 20m, AppointmentStatus.Confirmed, 4),
            At(Luigi, "Barba", 12m, AppointmentStatus.NoShow, 5),
            At(Luigi, "Barba", 12m, AppointmentStatus.Confirmed, 25),
        };

        var report = FinanceCalculator.Build(November, null, Rome, Now, appointments, [], [], new Dictionary<Guid, string> { [Mario] = "Mario", [Luigi] = "Luigi" });

        Assert.Equal(45m, report.Totals.Income);
        Assert.Equal((2, 1), (report.Totals.Appointments, report.Totals.NoShows));
        Assert.Equal(12m, report.ExpectedIncome);
        Assert.Equal(1, report.Unmarked);
        var mario = report.Staff[0];
        Assert.Equal(("Mario", 2, 45m), (mario.Name, mario.Appointments, mario.Sales));
        Assert.Equal(new FinanceServiceLineDto("Taglio", 2, 45m), Assert.Single(mario.Services));
        Assert.Equal((0, 1), (report.Staff[1].Appointments, report.Staff[1].NoShows));
        Assert.Equal(25m, report.Days[2].Income);
    }

    [Fact]
    public void Fixed_expenses_repeat_every_month_until_they_end_and_variable_ones_count_once()
    {
        var rent = Expense.Create(ExpenseKind.Fixed, "Affitto", 800m, null, new DateOnly(2026, 9, 15), new DateOnly(2026, 11, 3));
        var products = Expense.Create(ExpenseKind.Variable, "Prodotti", 120m, null, new DateOnly(2026, 11, 10), null);
        var old = Expense.Create(ExpenseKind.Variable, "Ottobre", 50m, null, new DateOnly(2026, 10, 31), null);
        var appointments = new[] { At(Mario, "Taglio", 1000m, AppointmentStatus.Completed, 2) };

        var november = FinanceCalculator.Totals(appointments, [rent, products, old], November, Now);
        var december = FinanceCalculator.Totals([], [rent, products, old], November.AddMonths(1), Now);

        Assert.Equal((800m, 120m, 80m), (november.FixedExpenses, november.VariableExpenses, november.Net));
        Assert.Equal((0m, 0m), (december.FixedExpenses, december.VariableExpenses));
        Assert.Equal(new DateOnly(2026, 9, 1), rent.Date);
    }
}
