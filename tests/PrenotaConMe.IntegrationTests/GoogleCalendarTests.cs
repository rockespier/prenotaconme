using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PrenotaConMe.Modules.Calendar;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Tenancy;
using PrenotaConMe.Shared.Calendar;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F2-07..09: optional Google Calendar sync per business, with Google replaced by <see cref="FakeGoogleCalendarApi"/>.</summary>
public sealed class GoogleCalendarTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, Guid Haircut, Guid StaffId);

    /// <summary>A business whose owner is also its only staff member (linked at once: same e-mail), 09–12 on <see cref="Day"/>.</summary>
    private async Task<Setup> CreateAsync(bool syncOn)
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Owner", $"{business.OwnerUid}@test.local", true, [location.Id], [haircut.Id]), TestJson.Options);
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!;
        (await business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}/working-hours",
            new[] { new WorkingHoursDto(location.Id, Day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();
        if (syncOn)
        {
            await SetSyncAsync(business, true);
        }

        return new Setup(business, location.Id, haircut.Id, staff.Id);
    }

    private static async Task SetSyncAsync(TestBusiness business, bool on) =>
        (await business.Owner.PutAsJsonAsync("/api/tenancy/tenants/current/settings",
            new UpdateTenantSettingsRequest(GoogleCalendarSync: on), TestJson.Options)).EnsureSuccessStatusCode();

    /// <summary>Runs the OAuth round trip: connect → (Google) → callback. Returns the redirect target and the code used.</summary>
    private async Task<(Uri Redirect, string Code)> ConnectAsync(Setup s)
    {
        var connect = await s.Business.Owner.PostAsync(new Uri("/api/calendar/google/connect", UriKind.Relative), null);
        connect.EnsureSuccessStatusCode();
        var url = new Uri((await connect.Content.ReadFromJsonAsync<GoogleConnectDto>(TestJson.Options))!.Url);
        var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"]!;
        var code = $"c{Guid.NewGuid():N}";

        var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var callback = await browser.GetAsync(new Uri(
            $"/api/calendar/google/callback?code={code}&state={Uri.EscapeDataString(state)}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return (callback.Headers.Location!, code);
    }

    private static DateTimeOffset RomeTime(int hour, int minute = 0)
    {
        var local = Day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    private async Task<IReadOnlyList<string>> FreeTimesAsync(Setup s) =>
        (await factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug).GetFromJsonAsync<AvailabilityDto>(
            $"/api/scheduling/availability?locationId={s.LocationId}&serviceId={s.Haircut}&from={Day:yyyy-MM-dd}&days=1",
            TestJson.Options))!.Days[0].Slots.Select(x => x.Time).ToList();

    [Fact]
    public async Task A_staff_member_connects_and_appointments_follow_in_their_calendar()
    {
        var s = await CreateAsync(syncOn: true);
        var before = await s.Business.GetAsync<GoogleCalendarStatusDto>("/api/calendar/google/status");

        var (redirect, code) = await ConnectAsync(s);
        var after = await s.Business.GetAsync<GoogleCalendarStatusDto>("/api/calendar/google/status");

        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);
        var booked = await anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut, s.StaffId, RomeTime(10)), TestJson.Options);
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;
        await FakeGoogleCalendarApi.WaitUntilAsync(() => factory.Google.Events.Values.Any(e => e.AppointmentId == appointment.Id));
        var exported = factory.Google.Events.Values.Single(e => e.AppointmentId == appointment.Id);

        (await anna.PostAsync(new Uri($"/api/scheduling/appointments/{appointment.Id}/cancel", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        await FakeGoogleCalendarApi.WaitUntilAsync(() => factory.Google.Events.Values.All(e => e.AppointmentId != appointment.Id));

        Assert.Equal((true, true, true, false), (before.Configured, before.EnabledByBusiness, before.HasStaffProfile, before.Connected));
        Assert.EndsWith("/admin?google=connected", redirect.ToString(), StringComparison.Ordinal);
        Assert.True(after.Connected);
        Assert.Equal($"{code}@gmail.com", after.GoogleEmail);
        Assert.StartsWith("Taglio capelli · anna-", exported.Summary, StringComparison.Ordinal);
        Assert.Equal(RomeTime(10), exported.Start);
    }

    [Fact]
    public async Task Busy_times_in_google_block_free_slots_only_while_the_business_uses_the_sync()
    {
        var s = await CreateAsync(syncOn: true);
        var (_, code) = await ConnectAsync(s);
        factory.Google.Busy[FakeGoogleCalendarApi.AccessToken(code)] = [new BusyInterval(RomeTime(10), RomeTime(11))];

        var withSync = await FreeTimesAsync(s);
        await SetSyncAsync(s.Business, false);
        var withoutSync = await FreeTimesAsync(s);

        Assert.Equal(["09:00", "09:30", "11:00", "11:30"], withSync);
        Assert.Contains("10:00", withoutSync);
        Assert.Contains("10:30", withoutSync);
    }

    [Fact]
    public async Task Without_the_business_setting_staff_cannot_connect()
    {
        var s = await CreateAsync(syncOn: false);

        var connect = await s.Business.Owner.PostAsync(new Uri("/api/calendar/google/connect", UriKind.Relative), null);
        var status = await s.Business.GetAsync<GoogleCalendarStatusDto>("/api/calendar/google/status");

        Assert.Equal(HttpStatusCode.Conflict, connect.StatusCode);
        Assert.False(status.EnabledByBusiness);
    }

    [Fact]
    public async Task Disconnecting_revokes_the_access()
    {
        var s = await CreateAsync(syncOn: true);
        var (_, code) = await ConnectAsync(s);

        (await s.Business.Owner.DeleteAsync(new Uri("/api/calendar/google/connection", UriKind.Relative))).EnsureSuccessStatusCode();
        var status = await s.Business.GetAsync<GoogleCalendarStatusDto>("/api/calendar/google/status");

        Assert.False(status.Connected);
        Assert.Contains("refresh-" + code, factory.Google.Revoked);
    }

    private static DateOnly NextWednesday()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Wednesday)
        {
            day = day.AddDays(1);
        }

        return day;
    }
}
