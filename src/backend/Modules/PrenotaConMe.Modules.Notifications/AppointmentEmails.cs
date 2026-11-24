using System.Globalization;
using System.Net;
using System.Text;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications;

/// <summary>F1-11: confirmation e-mails (client and staff member) with the event as an .ics attachment.</summary>
internal static class AppointmentEmails
{
    private sealed record Texts(
        string Culture, string ClientSubject, string StaffSubject, string Hello, string HelloNoName,
        string ClientIntro, string StaffIntro, string Service, string When, string With, string Where,
        string Price, string PayOnSite, string Client, string CalendarHint, string Footer);

    private static readonly Dictionary<string, Texts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new(
            "it-IT",
            "Prenotazione confermata: {0}, {1} alle {2}",
            "Nuova prenotazione: {0}, {1} alle {2}",
            "Ciao {0},", "Ciao,",
            "la tua prenotazione presso {0} è confermata.",
            "hai una nuova prenotazione.",
            "Servizio", "Quando", "Con", "Dove", "Prezzo", "da pagare in sede", "Cliente",
            "In allegato trovi l'appuntamento da aggiungere al tuo calendario.",
            "Email inviata automaticamente da PrenotaConMe."),
        ["es"] = new(
            "es-ES",
            "Reserva confirmada: {0}, {1} a las {2}",
            "Nueva reserva: {0}, {1} a las {2}",
            "Hola {0}:", "Hola:",
            "tu reserva en {0} está confirmada.",
            "tienes una nueva reserva.",
            "Servicio", "Cuándo", "Con", "Dónde", "Precio", "se paga en el local", "Cliente",
            "Adjuntamos la cita para que la añadas a tu calendario.",
            "Correo enviado automáticamente por PrenotaConMe."),
        ["en"] = new(
            "en-GB",
            "Booking confirmed: {0}, {1} at {2}",
            "New booking: {0}, {1} at {2}",
            "Hi {0},", "Hi,",
            "your booking at {0} is confirmed.",
            "you have a new booking.",
            "Service", "When", "With", "Where", "Price", "paid on site", "Client",
            "The appointment is attached so you can add it to your calendar.",
            "E-mail sent automatically by PrenotaConMe."),
    };

    public static IReadOnlyList<EmailMessage> Build(AppointmentNotice n, string? staffEmailOverride, DateTimeOffset now)
    {
        var t = ByLanguage.GetValueOrDefault(n.Language) ?? ByLanguage["it"];
        var culture = CultureInfo.GetCultureInfo(t.Culture);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(n.TimeZone);
        var local = TimeZoneInfo.ConvertTime(n.StartsAt, tz);
        var date = local.ToString("dddd d MMMM yyyy", culture);
        var time = local.ToString("HH:mm", culture);
        var place = string.Join(", ", new[] { n.LocationName, n.LocationAddress, n.LocationCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var price = FormatPrice(n.Price, culture);
        var uid = $"{n.AppointmentId}@prenotaconme";

        var messages = new List<EmailMessage>(2);

        if (!string.IsNullOrWhiteSpace(n.ClientEmail))
        {
            var rows = new List<(string, string)>
            {
                (t.Service, n.ServiceName),
                (t.When, $"{date}, {time}"),
                (t.With, n.StaffName),
                (t.Where, place),
                (t.Price, $"{price} ({t.PayOnSite})"),
            };
            if (!string.IsNullOrWhiteSpace(n.LocationPhone))
            {
                rows.Add(("☎", n.LocationPhone));
            }

            var ics = IcsCalendar.Build(uid, now, n.StartsAt, n.EndsAt,
                $"{n.ServiceName} – {n.BusinessName}", place, $"{t.With}: {n.StaffName}");
            messages.Add(Compose(
                n.ClientEmail, n.ClientName, n.BusinessName,
                Format(culture, t.ClientSubject, n.ServiceName, date, time),
                Greeting(t, n.ClientName), Format(culture, t.ClientIntro, n.BusinessName), rows, t, ics));
        }

        var staffEmail = string.IsNullOrWhiteSpace(staffEmailOverride) ? n.StaffEmail : staffEmailOverride;
        if (!string.IsNullOrWhiteSpace(staffEmail))
        {
            var client = string.Join(" ", new[] { n.ClientName, string.IsNullOrWhiteSpace(n.ClientEmail) ? null : $"<{n.ClientEmail}>" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var rows = new List<(string, string)>
            {
                (t.Service, n.ServiceName),
                (t.When, $"{date}, {time}"),
                (t.Client, client),
                (t.Where, place),
                (t.Price, price),
            };

            var ics = IcsCalendar.Build(uid, now, n.StartsAt, n.EndsAt,
                $"{n.ServiceName} – {n.ClientName ?? n.ClientEmail}", place, $"{t.Client}: {client}");
            messages.Add(Compose(
                staffEmail, n.StaffName, n.BusinessName,
                Format(culture, t.StaffSubject, n.ServiceName, date, time),
                Greeting(t, n.StaffName), t.StaffIntro, rows, t, ics));
        }

        return messages;
    }

    private static EmailMessage Compose(
        string to, string? toName, string businessName, string subject, string greeting, string intro,
        IReadOnlyList<(string Label, string Value)> rows, Texts t, string ics)
    {
        var text = new StringBuilder()
            .AppendLine(greeting)
            .AppendLine(intro)
            .AppendLine();
        foreach (var (label, value) in rows)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }

        text.AppendLine().AppendLine(t.CalendarHint).AppendLine().AppendLine("— " + businessName).AppendLine(t.Footer);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222;max-width:560px\">")
            .Append("<p>").Append(Encode(greeting)).Append("<br>").Append(Encode(intro)).Append("</p>")
            .Append("<table cellpadding=\"6\" style=\"border-collapse:collapse\">");
        foreach (var (label, value) in rows)
        {
            html.Append("<tr><td style=\"color:#666;vertical-align:top\">").Append(Encode(label))
                .Append("</td><td><strong>").Append(Encode(value)).Append("</strong></td></tr>");
        }

        html.Append("</table>")
            .Append("<p>").Append(Encode(t.CalendarHint)).Append("</p>")
            .Append("<p>— ").Append(Encode(businessName)).Append("</p>")
            .Append("<p style=\"color:#999;font-size:12px\">").Append(Encode(t.Footer)).Append("</p>")
            .Append("</div>");

        return new EmailMessage(
            to, toName, businessName, subject, text.ToString(), html.ToString(),
            [new EmailAttachment("appuntamento.ics", IcsCalendar.ContentType, Encoding.UTF8.GetBytes(ics))]);
    }

    private static string Greeting(Texts t, string? name) =>
        string.IsNullOrWhiteSpace(name) ? t.HelloNoName : string.Format(CultureInfo.InvariantCulture, t.Hello, name);

    private static string Format(CultureInfo culture, string format, params object[] args) => string.Format(culture, format, args);

    /// <summary>Prices are always euros, whatever the language.</summary>
    private static string FormatPrice(decimal price, CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return price.ToString("C", format);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
