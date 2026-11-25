using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F3-T1/T2: the business bot, linking a chat with the deep link, and appointment messages on Telegram.</summary>
public sealed class TelegramTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private sealed record Setup(TestBusiness Business, HttpClient Anna, string Token, string WebhookPath, string Secret);

    /// <summary>Business with a bot (webhook mode) and client Anna, who has one appointment.</summary>
    private async Task<(Setup Setup, AppointmentDto Appointment)> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Wednesday)
        {
            day = day.AddDays(1);
        }

        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        var anna = factory.CreateUserClient($"anna-{business.Slug}", business.Slug);
        var local = day.ToDateTime(new TimeOnly(10, 0));
        var booked = await anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(location.Id, haircut.Id, mario, new DateTimeOffset(local, Rome.GetUtcOffset(local))), TestJson.Options);
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;

        var token = $"valid{Guid.NewGuid():N}:secret";
        (await business.Owner.PutAsJsonAsync("/api/notifications/telegram/bot", new SaveTelegramBotRequest(token), TestJson.Options))
            .EnsureSuccessStatusCode();
        var (url, secret) = factory.Telegram.Webhooks[token];
        return (new Setup(business, anna, token, new Uri(url).AbsolutePath, secret), appointment);
    }

    private Task<HttpResponseMessage> SendUpdateAsync(Setup s, long chatId, string text, string? secret = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(s.WebhookPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new { update_id = Random.Shared.Next(), message = new { chat = new { id = chatId }, text } }),
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret ?? s.Secret);
        return factory.CreateClient().SendAsync(request);
    }

    private static async Task<long> LinkAsync(Setup s, HttpClient user, long chatId, FakeTelegramApi telegram, ApiFactory factory)
    {
        var link = await user.PostAsync(new Uri("/api/notifications/telegram/link", UriKind.Relative), null);
        var url = (await link.Content.ReadFromJsonAsync<TelegramLinkUrlDto>(TestJson.Options))!.Url;
        var code = url[(url.IndexOf("?start=", StringComparison.Ordinal) + 7)..];
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(s.WebhookPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new { update_id = 1, message = new { chat = new { id = chatId }, text = "/start " + code } }),
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", s.Secret);
        (await factory.CreateClient().SendAsync(request)).EnsureSuccessStatusCode();
        await telegram.WaitForAsync(chatId, 1);
        return chatId;
    }

    [Fact]
    public async Task Client_links_the_chat_and_gets_appointment_changes_there()
    {
        var (s, appointment) = await CreateAsync();
        var chatId = Random.Shared.NextInt64(1_000_000, 9_000_000);

        var before = await s.Anna.GetFromJsonAsync<TelegramLinkDto>("/api/notifications/telegram/link", TestJson.Options);
        await LinkAsync(s, s.Anna, chatId, factory.Telegram, factory);
        var after = await s.Anna.GetFromJsonAsync<TelegramLinkDto>("/api/notifications/telegram/link", TestJson.Options);
        (await s.Business.Owner.PostAsync(new Uri($"/api/scheduling/calendar/appointments/{appointment.Id}/cancel", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        var messages = await factory.Telegram.WaitForAsync(chatId, 2);

        Assert.StartsWith("bot_", before!.BotUsername, StringComparison.Ordinal);
        Assert.False(before.Linked);
        Assert.True(after!.Linked);
        Assert.StartsWith("✅", messages[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Appuntamento annullato\n", messages[1].Text, StringComparison.Ordinal);
        Assert.EndsWith($"{ApiFactory.PublicUrl}/{s.Business.Slug}/prenotazioni", messages[1].Text, StringComparison.Ordinal);
        Assert.All(messages, m => Assert.Equal(s.Token, m.Token));
    }

    [Fact]
    public async Task Webhook_needs_the_secret_and_stop_or_bad_codes_are_answered()
    {
        var (s, _) = await CreateAsync();
        var chatId = Random.Shared.NextInt64(1_000_000, 9_000_000);

        var forged = await SendUpdateAsync(s, chatId, "/start whatever", secret: "wrong");
        (await SendUpdateAsync(s, chatId, "/start not-a-real-code")).EnsureSuccessStatusCode();
        await LinkAsync(s, s.Anna, chatId, factory.Telegram, factory);
        (await SendUpdateAsync(s, chatId, "/stop")).EnsureSuccessStatusCode();
        var after = await s.Anna.GetFromJsonAsync<TelegramLinkDto>("/api/notifications/telegram/link", TestJson.Options);
        var replies = await factory.Telegram.WaitForAsync(chatId, 3);

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Contains("non è più valido", replies[0].Text, StringComparison.Ordinal);
        Assert.Contains("scollegato", replies[2].Text, StringComparison.Ordinal);
        Assert.False(after!.Linked);
    }

    [Fact]
    public async Task Bot_token_is_checked_and_admin_only()
    {
        var (s, _) = await CreateAsync();

        var invalid = await s.Business.Owner.PutAsJsonAsync("/api/notifications/telegram/bot", new SaveTelegramBotRequest("nope"), TestJson.Options);
        var forbidden = await s.Anna.GetAsync(new Uri("/api/notifications/telegram/bot", UriKind.Relative));
        var bot = await s.Business.GetAsync<TelegramBotDto>("/api/notifications/telegram/bot");
        (await s.Business.Owner.DeleteAsync(new Uri("/api/notifications/telegram/bot", UriKind.Relative))).EnsureSuccessStatusCode();
        var gone = await s.Anna.GetFromJsonAsync<TelegramLinkDto>("/api/notifications/telegram/link", TestJson.Options);
        var webhookAfterDelete = await SendUpdateAsync(s, 1, "/start");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(TelegramMode.Webhook, bot.Mode);
        Assert.StartsWith(ApiFactory.PublicUrl + "/api/notifications/telegram/webhook/", factory.Telegram.Webhooks[s.Token].Url, StringComparison.Ordinal);
        Assert.Null(gone!.BotUsername);
        Assert.Equal(HttpStatusCode.Unauthorized, webhookAfterDelete.StatusCode);
    }
}
