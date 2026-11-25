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
    public void Replies_follow_the_business_language_and_drop_a_missing_link() =>
        Assert.Equal("Por ahora aquí recibes los avisos de Barbería. Para reservar:",
            TelegramTexts.Format(TelegramTexts.For("es").NotYet, "Barbería", null));
}
