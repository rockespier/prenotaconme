using System.Text;
using PrenotaConMe.Modules.Notifications;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.UnitTests.Notifications;

public sealed class AppointmentEmailsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    // Wednesday 2 December 2026, 10:30 in Rome (UTC+1).
    internal static AppointmentNotice Notice(string language = "it", string? clientEmail = "anna@example.com") => new(
        Guid.Parse("0193a000-0000-7000-8000-000000000001"), "Barberia <Demo>", "barberia-demo", language, "Taglio capelli", 20m,
        new DateTimeOffset(2026, 12, 2, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 2, 10, 0, 0, TimeSpan.Zero),
        "Europe/Rome", "Sede Centro", "Via Roma 1", "Milano", null, "Mario", "mario@example.com", Guid.NewGuid(),
        "Anna", clientEmail, Guid.NewGuid());

    [Fact]
    public void Client_and_staff_each_get_an_email_in_the_business_language_with_local_time()
    {
        var messages = AppointmentEmails.Build(Notice(), null, Now);

        Assert.Equal(2, messages.Count);
        var client = messages[0];
        Assert.Equal("anna@example.com", client.To);
        Assert.Equal("Barberia <Demo>", client.FromName);
        Assert.Equal("Prenotazione confermata: Taglio capelli, mercoledì 2 dicembre 2026 alle 10:30", client.Subject);
        Assert.Contains("Con: Mario", client.TextBody, StringComparison.Ordinal);
        Assert.Contains("Sede Centro, Via Roma 1, Milano", client.TextBody, StringComparison.Ordinal);
        Assert.Contains("Barberia &lt;Demo&gt;", client.HtmlBody, StringComparison.Ordinal); // user data is HTML-encoded

        var staff = messages[1];
        Assert.Equal("mario@example.com", staff.To);
        Assert.StartsWith("Nuova prenotazione:", staff.Subject, StringComparison.Ordinal);
        Assert.Contains("Cliente: Anna <anna@example.com>", staff.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_email_carries_the_appointment_as_ics()
    {
        var attachment = Assert.Single(AppointmentEmails.Build(Notice(), null, Now)[0].Attachments);
        var ics = Encoding.UTF8.GetString(attachment.Content);

        Assert.StartsWith("text/calendar", attachment.ContentType, StringComparison.Ordinal);
        Assert.Contains("UID:0193a000-0000-7000-8000-000000000001@prenotaconme\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("DTSTART:20261202T093000Z\r\n", ics, StringComparison.Ordinal);
        Assert.Contains("DTEND:20261202T100000Z\r\n", ics, StringComparison.Ordinal);
        Assert.Contains(@"LOCATION:Sede Centro\, Via Roma 1\, Milano" + "\r\n", ics, StringComparison.Ordinal);
    }

    [Fact]
    public void Staff_override_redirects_staff_email_and_missing_client_email_is_skipped()
    {
        var messages = AppointmentEmails.Build(Notice(clientEmail: null), "owner@example.com", Now);

        Assert.Equal("owner@example.com", Assert.Single(messages).To);
    }

    [Theory]
    [InlineData("es", "Reserva confirmada: Taglio capelli, miércoles 2 diciembre 2026 a las 10:30")]
    [InlineData("en", "Booking confirmed: Taglio capelli, Wednesday 2 December 2026 at 10:30")]
    [InlineData("xx", "Prenotazione confermata: Taglio capelli, mercoledì 2 dicembre 2026 alle 10:30")]
    public void Subject_follows_the_language(string language, string subject) =>
        Assert.Equal(subject, AppointmentEmails.Build(Notice(language), null, Now)[0].Subject);

    [Fact]
    public void Price_is_in_euros_in_every_language() =>
        Assert.Contains("€", AppointmentEmails.Build(Notice("en"), null, Now)[0].TextBody, StringComparison.Ordinal);
}

public sealed class IcsCalendarTests
{
    [Fact]
    public void Text_is_escaped_and_long_lines_are_folded_at_75_octets()
    {
        var start = new DateTimeOffset(2026, 12, 2, 9, 30, 0, TimeSpan.Zero);
        var ics = IcsCalendar.Build("id@x", start, start, start.AddHours(1), @"a;b,c\d", null, new string('è', 100));

        Assert.Contains(@"SUMMARY:a\;b\,c\\d" + "\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("LOCATION", ics, StringComparison.Ordinal);
        Assert.All(ics.Split("\r\n"), line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Contains("DESCRIPTION:" + new string('è', 100), ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", ics, StringComparison.Ordinal);
    }
}
