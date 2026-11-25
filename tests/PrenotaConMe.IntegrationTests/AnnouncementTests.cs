using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications.Announcements;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F2-03: Admin announcements, sent once to the clients and shown on the public pages while valid.</summary>
public sealed class AnnouncementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Rome).DateTime);

    /// <summary>Business with two clients (each booked once): Anna and Bea.</summary>
    private async Task<(TestBusiness Business, HttpClient Anna, HttpClient Bea)> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        var day = Today.AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Wednesday)
        {
            day = day.AddDays(1);
        }

        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        var anna = factory.CreateUserClient($"anna-{business.Slug}", business.Slug);
        var bea = factory.CreateUserClient($"bea-{business.Slug}", business.Slug);
        var hour = 9;
        foreach (var client in new[] { anna, bea })
        {
            var local = day.ToDateTime(new TimeOnly(hour++, 0));
            (await client.PostAsJsonAsync("/api/scheduling/appointments",
                new BookAppointmentRequest(location.Id, haircut.Id, mario, new DateTimeOffset(local, Rome.GetUtcOffset(local))), TestJson.Options))
                .EnsureSuccessStatusCode();
        }

        return (business, anna, bea);
    }

    private static async Task<AnnouncementDto> CreateAnnouncementAsync(
        TestBusiness business, string title, DateOnly from, DateOnly to, bool notify)
    {
        var response = await business.Owner.PostAsJsonAsync("/api/notifications/announcements",
            new CreateAnnouncementRequest(title, "Il 24 dicembre chiudiamo alle 14.\nMaggiorazione festiva del 10%.", from, to, notify), TestJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AnnouncementDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Sending_reaches_every_client_by_email_and_push_once()
    {
        var (business, anna, _) = await CreateAsync();
        var endpoint = $"https://push.example.com/anna-{Guid.NewGuid():N}";
        (await anna.PutAsJsonAsync("/api/notifications/push/subscriptions",
            new SavePushSubscriptionRequest(endpoint, "p256dh-key", "auth-key"), TestJson.Options)).EnsureSuccessStatusCode();
        var title = $"Orari di Natale {business.Slug}";

        var audience = await business.GetAsync<AnnouncementAudienceDto>("/api/notifications/announcements/audience");
        var sent = await CreateAnnouncementAsync(business, title, Today, Today.AddDays(30), notify: true);
        var again = await business.Owner.PostAsync(new Uri($"/api/notifications/announcements/{sent.Id}/send", UriKind.Relative), null);

        var emails = await factory.Emails.WaitForAsync(e => e.Subject == title, 2);
        var push = Assert.Single(await factory.Pushes.WaitForAsync(endpoint, 1));

        Assert.Equal(new AnnouncementAudienceDto(2, 2), audience);
        Assert.Equal(2, sent.RecipientCount);
        Assert.NotNull(sent.SentAt);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(2, emails.Count);
        Assert.All(emails, e => Assert.Contains("Maggiorazione festiva del 10%.", e.TextBody, StringComparison.Ordinal));
        Assert.Contains("<br>", emails[0].HtmlBody, StringComparison.Ordinal);
        Assert.Equal(title, push.Title);
        Assert.Equal($"/{business.Slug}", push.Url);
    }

    [Fact]
    public async Task Public_pages_show_only_todays_announcements_of_that_business()
    {
        var (business, _, _) = await CreateAsync();
        var other = await TestBusiness.CreateAsync(factory);
        await CreateAnnouncementAsync(business, "Oggi", Today.AddDays(-1), Today, notify: false);
        await CreateAnnouncementAsync(business, "Domani", Today.AddDays(1), Today.AddDays(2), notify: false);
        await CreateAnnouncementAsync(other, "Altro negozio", Today, Today, notify: false);

        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", business.Slug);
        var current = await anonymous.GetFromJsonAsync<List<PublicAnnouncementDto>>("/api/notifications/announcements/current", TestJson.Options);
        var all = await business.GetAsync<List<AnnouncementDto>>("/api/notifications/announcements");

        Assert.Equal("Oggi", Assert.Single(current!).Title);
        Assert.Equal(2, all.Count);
        Assert.All(all, a => Assert.Null(a.SentAt));
    }

    [Fact]
    public async Task Announcements_are_validated_and_admin_only()
    {
        var (business, anna, _) = await CreateAsync();

        var invalid = await business.Owner.PostAsJsonAsync("/api/notifications/announcements",
            new CreateAnnouncementRequest(" ", "x", Today, Today.AddDays(-1), false), TestJson.Options);
        var forbidden = await anna.PostAsJsonAsync("/api/notifications/announcements",
            new CreateAnnouncementRequest("Ciao", "x", Today, Today, false), TestJson.Options);
        var saved = await CreateAnnouncementAsync(business, "Bozza", Today, Today, notify: false);
        var updated = await business.Owner.PutAsJsonAsync($"/api/notifications/announcements/{saved.Id}",
            new UpdateAnnouncementRequest("Chiuso", "Per ferie", Today, Today.AddDays(3)), TestJson.Options);
        var deleted = await business.Owner.DeleteAsync(new Uri($"/api/notifications/announcements/{saved.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("announcement.title.required", problem, StringComparison.Ordinal);
        Assert.Contains("announcement.dates.invalid", problem, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("Chiuso", (await updated.Content.ReadFromJsonAsync<AnnouncementDto>(TestJson.Options))!.Title);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
