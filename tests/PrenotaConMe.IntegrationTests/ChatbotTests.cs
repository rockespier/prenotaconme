using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications.Telegram;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;
using static PrenotaConMe.IntegrationTests.ScriptedChatModel;

namespace PrenotaConMe.IntegrationTests;

/// <summary>
/// F3-03: booking by Telegram chat. The model is scripted (<see cref="ScriptedChatModel"/>); the tools, the
/// confirmation rule and the bookings are real.
/// </summary>
public sealed class ChatbotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Setup(TestBusiness Business, HttpClient Anna, Guid Haircut, Guid Mario, DateOnly Day, string WebhookPath, string Secret);

    private async Task<Setup> CreateAsync()
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

        var token = $"valid{Guid.NewGuid():N}:secret";
        (await business.Owner.PutAsJsonAsync("/api/notifications/telegram/bot", new SaveTelegramBotRequest(token), TestJson.Options))
            .EnsureSuccessStatusCode();
        var (url, secret) = factory.Telegram.Webhooks[token];
        var anna = factory.CreateUserClient($"anna-{business.Slug}", business.Slug);
        return new Setup(business, anna, haircut.Id, mario, day, new Uri(url).AbsolutePath, secret);
    }

    private async Task SendAsync(Setup s, long chatId, string text)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(s.WebhookPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new { update_id = Random.Shared.Next(), message = new { chat = new { id = chatId }, text } }),
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", s.Secret);
        (await factory.CreateClient().SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// <summary>Links Anna's account to the chat (the "✅" reply is message 1).</summary>
    private async Task<long> LinkAnnaAsync(Setup s)
    {
        var chatId = Random.Shared.NextInt64(1_000_000, 9_000_000);
        var link = await s.Anna.PostAsync(new Uri("/api/notifications/telegram/link", UriKind.Relative), null);
        var url = (await link.Content.ReadFromJsonAsync<TelegramLinkUrlDto>(TestJson.Options))!.Url;
        await SendAsync(s, chatId, "/start " + url[(url.IndexOf("?start=", StringComparison.Ordinal) + 7)..]);
        await factory.Telegram.WaitForAsync(chatId, 1);
        return chatId;
    }

    private static async Task<IReadOnlyList<MyAppointmentDto>> AnnaAppointmentsAsync(Setup s) =>
        (await s.Anna.GetFromJsonAsync<MyAppointmentsDto>("/api/scheduling/appointments/mine", TestJson.Options))!.Appointments;

    [Fact]
    public async Task Client_books_by_chat_only_after_confirming_in_a_later_message()
    {
        var s = await CreateAsync();
        var chatId = await LinkAnnaAsync(s);
        var date = s.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var book = new { service_id = s.Haircut, start = $"{date}T10:00", staff_id = s.Mario };

        // The model "forgets" to wait and calls book twice in the same message: still only a proposal.
        factory.ChatModel.Script(
            Tool("get_availability", new { service_id = s.Haircut, date }),
            Tool("book", book),
            Tool("book", book),
            Say("Taglio mercoledì alle 10:00 con Mario, confermi?"));
        await SendAsync(s, chatId, "Vorrei un taglio mercoledì alle 10 con Mario");
        var question = await factory.Telegram.WaitForAsync(chatId, 2);
        var afterQuestion = await AnnaAppointmentsAsync(s);
        var proposal = factory.ChatModel.ToolResults();

        factory.ChatModel.Script(Tool("book", book), Say("Fatto! Ti aspettiamo."));
        await SendAsync(s, chatId, "Sì");
        var done = await factory.Telegram.WaitForAsync(chatId, 3);
        var booked = Assert.Single(await AnnaAppointmentsAsync(s));
        var calls = factory.ChatModel.Calls.ToList();

        Assert.Equal("Taglio mercoledì alle 10:00 con Mario, confermi?", question[1].Text);
        Assert.Empty(afterQuestion);
        Assert.Contains("\"10:00\"", proposal[0], StringComparison.Ordinal);
        Assert.All(proposal.Skip(1), r => Assert.Contains("confirmation_required", r, StringComparison.Ordinal));
        Assert.Equal("Fatto! Ti aspettiamo.", done[2].Text);
        Assert.Contains("\"booked\"", factory.ChatModel.ToolResults()[^1], StringComparison.Ordinal);
        Assert.Equal((s.Mario, AppointmentStatus.Confirmed), (booked.StaffMemberId, booked.Status));
        Assert.Contains("anna-", calls[0].SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_cancels_by_chat_after_confirming()
    {
        var s = await CreateAsync();
        var chatId = await LinkAnnaAsync(s);
        var local = s.Day.ToDateTime(new TimeOnly(11, 0));
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var location = (await s.Business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        (await s.Anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(location.Id, s.Haircut, s.Mario, new DateTimeOffset(local, rome.GetUtcOffset(local))), TestJson.Options))
            .EnsureSuccessStatusCode();
        var appointment = (await AnnaAppointmentsAsync(s))[0];

        factory.ChatModel.Script(
            Tool("my_appointments", new { }),
            Tool("cancel", new { appointment_id = appointment.Id }),
            Say("Vuoi annullare il taglio di mercoledì alle 11?"));
        await SendAsync(s, chatId, "Devo disdire");
        await factory.Telegram.WaitForAsync(chatId, 2);
        var before = (await AnnaAppointmentsAsync(s))[0].Status;

        factory.ChatModel.Script(Tool("cancel", new { appointment_id = appointment.Id }), Say("Annullato."));
        await SendAsync(s, chatId, "sì");
        await factory.Telegram.WaitForAsync(chatId, 3);
        var after = (await AnnaAppointmentsAsync(s))[0].Status;

        Assert.Equal(AppointmentStatus.Confirmed, before);
        Assert.Equal(AppointmentStatus.Cancelled, after);
    }

    [Fact]
    public async Task Unlinked_chats_are_asked_to_link_their_account()
    {
        var s = await CreateAsync();
        var chatId = Random.Shared.NextInt64(1_000_000, 9_000_000);

        await SendAsync(s, chatId, "Ciao, posso prenotare?");
        var replies = await factory.Telegram.WaitForAsync(chatId, 1);

        Assert.StartsWith("Per prenotare qui in chat collega prima il tuo account", replies[0].Text, StringComparison.Ordinal);
        Assert.EndsWith($"/{s.Business.Slug}/prenotazioni", replies[0].Text, StringComparison.Ordinal);
    }
}
