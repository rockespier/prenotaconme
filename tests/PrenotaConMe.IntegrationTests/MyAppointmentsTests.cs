using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-12: a client lists, cancels and reschedules their own appointments, within the business notice.</summary>
public sealed class MyAppointmentsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>A Wednesday 7–13 days ahead (inside the default opening hours).</summary>
    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, Guid Mario, HttpClient Anna);

    private async Task<Setup> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");

        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, Day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        return new Setup(business, location.Id, haircut, mario, factory.CreateUserClient($"anna-{business.Slug}", business.Slug));
    }

    private static async Task<AppointmentDto> BookAsync(Setup s, HttpClient client, int hour, int minute = 0)
    {
        var response = await client.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(hour, minute)), TestJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;
    }

    private static Task<MyAppointmentsDto?> MineAsync(HttpClient client) =>
        client.GetFromJsonAsync<MyAppointmentsDto>("/api/scheduling/appointments/mine", TestJson.Options);

    private static Task<HttpResponseMessage> RescheduleAsync(HttpClient client, Guid id, int hour, int minute = 0) =>
        client.PostAsJsonAsync($"/api/scheduling/appointments/{id}/reschedule",
            new RescheduleAppointmentRequest(RomeTime(hour, minute), null), TestJson.Options);

    private static DateTimeOffset RomeTime(int hour, int minute = 0)
    {
        var local = Day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    [Fact]
    public async Task Lists_only_the_callers_appointments_with_details()
    {
        var s = await CreateAsync();
        var mine = await BookAsync(s, s.Anna, 9);
        await BookAsync(s, factory.CreateUserClient($"luca-{s.Business.Slug}", s.Business.Slug), 10);

        var list = await MineAsync(s.Anna);

        Assert.Equal(2, list!.CancellationNoticeHours);
        var item = Assert.Single(list.Appointments);
        Assert.Equal(mine.Id, item.Id);
        Assert.Equal("Mario", item.StaffName);
        Assert.Equal("Europe/Rome", item.TimeZone);
        Assert.True(item.CanChange);
        Assert.Equal(RomeTime(9).AddHours(-2), item.ChangeableUntil);
    }

    [Fact]
    public async Task Cancel_frees_the_slot()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, s.Anna, 9);

        var cancelled = await s.Anna.PostAsync(new Uri($"/api/scheduling/appointments/{appointment.Id}/cancel", UriKind.Relative), null);
        var again = await s.Anna.PostAsync(new Uri($"/api/scheduling/appointments/{appointment.Id}/cancel", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(AppointmentStatus.Cancelled, (await MineAsync(s.Anna))!.Appointments.Single().Status);
        await BookAsync(s, factory.CreateUserClient($"luca-{s.Business.Slug}", s.Business.Slug), 9);
    }

    [Fact]
    public async Task Someone_elses_appointment_is_not_found()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, s.Anna, 9);
        var luca = factory.CreateUserClient($"luca-{s.Business.Slug}", s.Business.Slug);

        var cancel = await luca.PostAsync(new Uri($"/api/scheduling/appointments/{appointment.Id}/cancel", UriKind.Relative), null);
        var move = await RescheduleAsync(luca, appointment.Id, 11);

        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
    }

    [Fact]
    public async Task Changes_are_refused_after_the_notice_deadline()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, s.Anna, 9);
        // The appointment is at most 14 days ahead: a 30-day notice puts it past the deadline.
        (await s.Business.Owner.PutAsJsonAsync("/api/tenancy/tenants/current/settings", new UpdateTenantSettingsRequest(720)))
            .EnsureSuccessStatusCode();

        var cancel = await s.Anna.PostAsync(new Uri($"/api/scheduling/appointments/{appointment.Id}/cancel", UriKind.Relative), null);
        var move = await RescheduleAsync(s.Anna, appointment.Id, 11);
        var list = await MineAsync(s.Anna);

        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Contains("appointment.tooLate", await cancel.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        Assert.False(list!.Appointments.Single().CanChange);
    }

    [Fact]
    public async Task Notice_setting_is_admin_only_and_validated()
    {
        var s = await CreateAsync();

        var asClient = await s.Anna.PutAsJsonAsync("/api/tenancy/tenants/current/settings", new UpdateTenantSettingsRequest(4));
        var invalid = await s.Business.Owner.PutAsJsonAsync("/api/tenancy/tenants/current/settings", new UpdateTenantSettingsRequest(-1));

        Assert.Equal(HttpStatusCode.Forbidden, asClient.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Reschedule_moves_to_a_free_slot_including_an_overlapping_one_of_its_own()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, s.Anna, 9);

        // 09:30 overlaps the current 09:00–10:00 haircut slot of the same person: it must count as free.
        var availability = await s.Anna.GetFromJsonAsync<AvailabilityDto>(
            $"/api/scheduling/availability?locationId={s.LocationId}&serviceId={s.Haircut.Id}&from={Day:yyyy-MM-dd}&days=1&rescheduling={appointment.Id}",
            TestJson.Options);
        var moved = await RescheduleAsync(s.Anna, appointment.Id, 9, 30);
        var body = await moved.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options);

        Assert.Contains(availability!.Days.Single().Slots, slot => slot.Time == "09:00");
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(RomeTime(9, 30), body!.StartsAt);
        Assert.Equal(RomeTime(9, 30).AddMinutes(s.Haircut.DurationMinutes), body.EndsAt);
        Assert.Equal(s.Mario, body.StaffMemberId);
    }

    [Fact]
    public async Task Reschedule_to_a_taken_slot_is_refused()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, s.Anna, 9);
        await BookAsync(s, factory.CreateUserClient($"luca-{s.Business.Slug}", s.Business.Slug), 11);

        var moved = await RescheduleAsync(s.Anna, appointment.Id, 11);

        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal(RomeTime(9), (await MineAsync(s.Anna))!.Appointments.Single().StartsAt);
    }

    private static DateOnly NextWednesday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (date.DayOfWeek != DayOfWeek.Wednesday)
        {
            date = date.AddDays(1);
        }

        return date;
    }
}
