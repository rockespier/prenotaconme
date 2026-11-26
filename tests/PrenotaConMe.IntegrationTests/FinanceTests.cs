using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Finance;
using PrenotaConMe.Modules.Finance.Domain;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F4-01..06: amount charged, expenses, monthly report and its export.</summary>
public sealed class FinanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, Guid Mario, string Month);

    /// <summary>A business with Mario and two appointments in the previous month (past, so they can be marked).</summary>
    private async Task<(Setup Setup, Guid First, Guid Second)> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;

        var month = DateTime.UtcNow.AddMonths(-1);
        var start = new DateTimeOffset(month.Year, month.Month, 10, 9, 0, 0, TimeSpan.Zero);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(
            (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(business.Slug))!.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = Appointment.BookForGuest(location.Id, haircut.Id, haircut.Name, haircut.Price, mario, "Anna", null, start, 30);
        var second = Appointment.BookForGuest(location.Id, haircut.Id, haircut.Name, haircut.Price, mario, "Bruno", null, start.AddHours(1), 30);
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        return (new Setup(business, location.Id, haircut, mario, month.ToString("yyyy-MM", CultureInfo.InvariantCulture)), first.Id, second.Id);
    }

    [Fact]
    public async Task Marked_outcomes_and_expenses_shape_the_monthly_report()
    {
        var (s, first, second) = await CreateAsync();

        var completed = await s.Business.Owner.PostAsJsonAsync($"/api/scheduling/calendar/appointments/{first}/outcome",
            new AppointmentOutcomeRequest(AppointmentStatus.Completed, s.Haircut.Price + 5), TestJson.Options);
        var noShow = await s.Business.Owner.PostAsJsonAsync($"/api/scheduling/calendar/appointments/{second}/outcome",
            new AppointmentOutcomeRequest(AppointmentStatus.NoShow, null), TestJson.Options);
        var monthStart = DateOnly.ParseExact(s.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        (await s.Business.Owner.PostAsJsonAsync("/api/finance/expenses",
            new SaveExpenseRequest(ExpenseKind.Fixed, "Affitto", 500m, null, monthStart.AddMonths(-2), null), TestJson.Options))
            .EnsureSuccessStatusCode();
        (await s.Business.Owner.PostAsJsonAsync("/api/finance/expenses",
            new SaveExpenseRequest(ExpenseKind.Variable, "Prodotti", 30m, s.LocationId, monthStart.AddDays(3), null), TestJson.Options))
            .EnsureSuccessStatusCode();

        var report = await s.Business.GetAsync<FinanceReportDto>($"/api/finance/report?month={s.Month}");
        var pdf = await s.Business.Owner.GetAsync(new Uri($"/api/finance/report/export?month={s.Month}&format=pdf", UriKind.Relative));
        var xlsx = await s.Business.Owner.GetAsync(new Uri($"/api/finance/report/export?month={s.Month}&format=xlsx", UriKind.Relative));
        var pdfBytes = await pdf.Content.ReadAsByteArrayAsync();
        var xlsxBytes = await xlsx.Content.ReadAsByteArrayAsync();

        completed.EnsureSuccessStatusCode();
        noShow.EnsureSuccessStatusCode();
        var income = s.Haircut.Price + 5;
        Assert.Equal(new FinanceTotalsDto(income, 500m, 30m, income - 530m, 1, 1), report.Totals);
        Assert.Equal(("Mario", 1, 1), (report.Staff[0].Name, report.Staff[0].Appointments, report.Staff[0].NoShows));
        Assert.Equal(2, report.Expenses.Count);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF"u8.ToArray(), pdfBytes[..4]);
        Assert.Equal("PK"u8.ToArray(), xlsxBytes[..2]);
    }

    [Fact]
    public async Task Future_appointments_cannot_be_marked_and_clients_cannot_mark_any()
    {
        var (s, first, _) = await CreateAsync();
        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        (await s.Business.Owner.PutAsJsonAsync($"/api/staff/{s.Mario}/working-hours",
            new[] { new WorkingHoursDto(s.LocationId, day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(18, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();
        var slots = (await anna.GetFromJsonAsync<AvailabilityDto>(
            $"/api/scheduling/availability?locationId={s.LocationId}&serviceId={s.Haircut.Id}&from={day:yyyy-MM-dd}&days=1", TestJson.Options))!;
        var booked = await anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, slots.Days[0].Slots[0].StartsAt), TestJson.Options);
        var future = (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;

        var tooEarly = await s.Business.Owner.PostAsJsonAsync($"/api/scheduling/calendar/appointments/{future.Id}/outcome",
            new AppointmentOutcomeRequest(AppointmentStatus.Completed, null), TestJson.Options);
        var byClient = await anna.PostAsJsonAsync($"/api/scheduling/calendar/appointments/{first}/outcome",
            new AppointmentOutcomeRequest(AppointmentStatus.Completed, null), TestJson.Options);

        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byClient.StatusCode);
    }
}
