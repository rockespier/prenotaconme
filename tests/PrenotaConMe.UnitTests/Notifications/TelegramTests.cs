using System.Text.Json;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Modules.Notifications.Telegram;

namespace PrenotaConMe.UnitTests.Notifications;

public sealed class TelegramTests
{
    [Fact]
    public void Update_parsing_reads_chat_and_text_and_tolerates_other_updates()
    {
        using var message = JsonDocument.Parse("""{"update_id":7,"message":{"chat":{"id":-100123},"text":"/start abc"}}""");
        using var edited = JsonDocument.Parse("""{"update_id":8,"edited_message":{}}""");

        Assert.Equal(new TelegramUpdate(7, -100123, "/start abc"), TelegramUpdate.Parse(message.RootElement));
        Assert.Equal(new TelegramUpdate(8, null, null), TelegramUpdate.Parse(edited.RootElement));
    }

    [Fact]
    public void Message_text_is_the_push_content_with_an_absolute_link()
    {
        var push = new PushNotification("Prenotazione confermata", "Taglio · mer 2 dic, 10:30", "/barberia/prenotazioni", "t");

        Assert.Equal("Prenotazione confermata\nTaglio · mer 2 dic, 10:30\nhttps://site.test/barberia/prenotazioni",
            TelegramJob.TextOf(push, "https://site.test/"));
        Assert.Equal("Prenotazione confermata\nTaglio · mer 2 dic, 10:30", TelegramJob.TextOf(push, null));
    }

    [Fact]
    public void Link_codes_fit_the_telegram_start_parameter()
    {
        var token = TelegramLinkToken.Create(Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        Assert.Equal(TelegramLinkToken.TokenLength, token.Token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token.Token);
        Assert.Equal(DateTimeOffset.UnixEpoch + TelegramLinkToken.Lifetime, token.ExpiresAt);
    }

    [Fact]
    public void Replies_follow_the_business_language_and_put_the_link_on_its_own_line()
    {
        Assert.Equal("Por ahora no puedo tomar reservas por chat: aquí recibes los avisos de Barbería. Para reservar usa el sitio.",
            TelegramTexts.Format(TelegramTexts.For("es").NotYet, "Barbería", null));
        Assert.EndsWith("usa el sitio.\nhttps://site.test/barberia",
            TelegramTexts.Format(TelegramTexts.For("es").NotYet, "Barbería", "https://site.test/barberia"), StringComparison.Ordinal);
    }
}
