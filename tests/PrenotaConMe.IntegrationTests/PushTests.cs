using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F2-02: devices subscribe to Web Push; clients and staff get a push when an appointment changes.</summary>
public sealed class PushTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, Guid Mario, HttpClient MarioUser, HttpClient Anna);

    /// <summary>Business with staff member Mario, linked to his own account, and client Anna.</summary>
    private async Task<Setup> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var marioUid = $"mario-{business.Slug}";
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"{marioUid}@test.local", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, Day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(18, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        // Signing in links the invitation (same verified e-mail).
        var marioUser = factory.CreateUserClient(marioUid, business.Slug);
        (await marioUser.GetAsync(new Uri("/api/users/me", UriKind.Relative))).EnsureSuccessStatusCode();

        return new Setup(business, location.Id, haircut, mario, marioUser, factory.CreateUserClient($"anna-{business.Slug}", business.Slug));
    }

    private static async Task<string> SubscribeAsync(HttpClient client, string name)
    {
        var endpoint = $"https://push.example.com/{name}-{Guid.NewGuid():N}";
        (await client.PutAsJsonAsync("/api/notifications/push/subscriptions",
            new SavePushSubscriptionRequest(endpoint, "p256dh-key", "auth-key"), TestJson.Options)).EnsureSuccessStatusCode();
        return endpoint;
    }

    private static DateTimeOffset RomeTime(int hour)
    {
        var local = Day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    [Fact]
    public async Task Booking_and_cancelling_push_the_other_party_but_not_who_acted()
    {
        var s = await CreateAsync();
        var annaPhone = await SubscribeAsync(s.Anna, "anna");
        var marioPhone = await SubscribeAsync(s.MarioUser, "mario");

        var booked = await s.Anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(10)), TestJson.Options);
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;

        var toMario = Assert.Single(await factory.Pushes.WaitForAsync(marioPhone, 1));
        Assert.Equal("Nuova prenotazione", toMario.Title);
        Assert.StartsWith("anna-", toMario.Body, StringComparison.Ordinal);
        Assert.Equal("/admin/calendario", toMario.Url);

        // The business cancels: both are told, the Admin (owner) is not linked to anyone here.
        (await s.Business.Owner.PostAsync(new Uri($"/api/scheduling/calendar/appointments/{appointment.Id}/cancel", UriKind.Relative), null))
            .EnsureSuccessStatusCode();

        var toAnna = Assert.Single(await factory.Pushes.WaitForAsync(annaPhone, 1));
        Assert.Equal("Appuntamento annullato", toAnna.Title);
        Assert.Equal($"/{s.Business.Slug}/prenotazioni", toAnna.Url);
        Assert.Equal(2, (await factory.Pushes.WaitForAsync(marioPhone, 2)).Count);
    }

    [Fact]
    public async Task Moving_to_another_staff_member_tells_both_of_them()
    {
        var s = await CreateAsync();
        var marioPhone = await SubscribeAsync(s.MarioUser, "mario");
        var luigiUid = $"luigi-{s.Business.Slug}";
        var created = await s.Business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Luigi", $"{luigiUid}@test.local", true, [s.LocationId], [s.Haircut.Id]), TestJson.Options);
        var luigi = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        var luigiUser = factory.CreateUserClient(luigiUid, s.Business.Slug);
        (await luigiUser.GetAsync(new Uri("/api/users/me", UriKind.Relative))).EnsureSuccessStatusCode();
        var luigiPhone = await SubscribeAsync(luigiUser, "luigi");

        var guest = await s.Business.Owner.PostAsJsonAsync("/api/scheduling/calendar/appointments",
            new CreateCalendarAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(11), "Walk-in", null), TestJson.Options);
        var appointment = (await guest.Content.ReadFromJsonAsync<CalendarAppointmentDto>(TestJson.Options))!;
        (await s.Business.Owner.PutAsJsonAsync($"/api/scheduling/calendar/appointments/{appointment.Id}",
            new MoveCalendarAppointmentRequest(luigi, RomeTime(12)), TestJson.Options)).EnsureSuccessStatusCode();

        var toMario = await factory.Pushes.WaitForAsync(marioPhone, 2);
        var toLuigi = Assert.Single(await factory.Pushes.WaitForAsync(luigiPhone, 1));
        Assert.Equal(["Nuova prenotazione", "Appuntamento annullato"], toMario.Select(p => p.Title).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal("Appuntamento spostato", toLuigi.Title);
    }

    [Fact]
    public async Task Subscriptions_are_validated_replaced_and_expired_ones_removed()
    {
        var s = await CreateAsync();
        var config = await factory.CreateClient().GetFromJsonAsync<PushConfigDto>("/api/notifications/push/config", TestJson.Options);
        var invalid = await s.Anna.PutAsJsonAsync("/api/notifications/push/subscriptions",
            new SavePushSubscriptionRequest("http://insecure.example.com/x", "k", "a"), TestJson.Options);

        var gone = await SubscribeAsync(s.MarioUser, "gone");
        var removed = await SubscribeAsync(s.MarioUser, "removed");
        (await s.MarioUser.DeleteAsync(new Uri($"/api/notifications/push/subscriptions?endpoint={Uri.EscapeDataString(removed)}", UriKind.Relative)))
            .EnsureSuccessStatusCode();
        var live = await SubscribeAsync(s.MarioUser, "live");

        (await s.Anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(14)), TestJson.Options)).EnsureSuccessStatusCode();
        await factory.Pushes.WaitForAsync(live, 1);
        (await s.Anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(15)), TestJson.Options)).EnsureSuccessStatusCode();

        Assert.Equal(ApiFactory.TestPushPublicKey, config!.PublicKey);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(2, (await factory.Pushes.WaitForAsync(live, 2)).Count);
        Assert.Empty(factory.Pushes.For(removed));
        Assert.Empty(factory.Pushes.For(gone));
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
