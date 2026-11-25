using System.Net;
using System.Text;

namespace PrenotaConMe.Modules.Notifications.Announcements;

/// <summary>F2-03: the e-mail version of an announcement, in the business's default language.</summary>
internal static class AnnouncementEmails
{
    private sealed record Texts(string Hello, string HelloNoName, string Book, string Why);

    private static readonly Dictionary<string, Texts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new("Ciao {0},", "Ciao,", "Prenota online", "Ricevi questa email perché hai prenotato presso {0} con PrenotaConMe."),
        ["es"] = new("Hola {0}:", "Hola:", "Reservar online", "Recibes este correo porque has reservado en {0} con PrenotaConMe."),
        ["en"] = new("Hi {0},", "Hi,", "Book online", "You are receiving this e-mail because you booked with {0} through PrenotaConMe."),
    };

    /// <param name="bookingUrl">Public booking page; null when the public URL is not configured.</param>
    public static EmailMessage Build(
        string to, string? toName, string businessName, string language, string title, string message, string? bookingUrl)
    {
        var t = ByLanguage.GetValueOrDefault(language) ?? ByLanguage["it"];
        var greeting = string.IsNullOrWhiteSpace(toName) ? t.HelloNoName : string.Format(System.Globalization.CultureInfo.InvariantCulture, t.Hello, toName);
        var why = string.Format(System.Globalization.CultureInfo.InvariantCulture, t.Why, businessName);

        var text = new StringBuilder()
            .AppendLine(greeting).AppendLine()
            .AppendLine(title).AppendLine()
            .AppendLine(message).AppendLine();
        if (bookingUrl is not null)
        {
            text.Append(t.Book).Append(": ").AppendLine(bookingUrl).AppendLine();
        }

        text.AppendLine("— " + businessName).AppendLine(why);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222;max-width:560px\">")
            .Append("<p>").Append(Encode(greeting)).Append("</p>")
            .Append("<h2 style=\"font-size:18px;margin:16px 0 8px\">").Append(Encode(title)).Append("</h2>")
            .Append("<p>").Append(Encode(message).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>");
        if (bookingUrl is not null)
        {
            html.Append("<p><a href=\"").Append(Encode(bookingUrl)).Append("\" style=\"color:#1a5fb4\">").Append(Encode(t.Book)).Append("</a></p>");
        }

        html.Append("<p>— ").Append(Encode(businessName)).Append("</p>")
            .Append("<p style=\"color:#999;font-size:12px\">").Append(Encode(why)).Append("</p>")
            .Append("</div>");

        return new EmailMessage(to, toName, businessName, title, text.ToString(), html.ToString(), []);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
