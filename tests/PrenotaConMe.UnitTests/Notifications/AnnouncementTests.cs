using PrenotaConMe.Modules.Notifications.Announcements;
using PrenotaConMe.Modules.Notifications.Domain;

namespace PrenotaConMe.UnitTests.Notifications;

public sealed class AnnouncementTests
{
    private static readonly DateOnly Day = new(2026, 12, 20);

    [Fact]
    public void Shown_between_the_dates_inclusive()
    {
        var a = Announcement.Create(" Natale ", " Chiuso ", Day, Day.AddDays(2));

        Assert.Equal("Natale", a.Title);
        Assert.False(a.IsShownOn(Day.AddDays(-1)));
        Assert.True(a.IsShownOn(Day));
        Assert.True(a.IsShownOn(Day.AddDays(2)));
        Assert.False(a.IsShownOn(Day.AddDays(3)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    [InlineData(-1, false)]
    public void Period_must_be_ordered_and_at_most_a_year(int days, bool valid) =>
        Assert.Equal(valid, Announcement.Validate("T", "M", Day, Day.AddDays(days)).IsValid);

    [Fact]
    public void Email_encodes_the_text_keeps_line_breaks_and_links_the_booking_page()
    {
        var email = AnnouncementEmails.Build(
            "anna@example.com", "Anna", "Barberia <Demo>", "es", "Navidad", "Cerrado <b>24</b>\nAbierto 26",
            "https://prenotaconme.example/barberia");

        Assert.Equal("Navidad", email.Subject);
        Assert.StartsWith("Hola Anna:", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Cerrado &lt;b&gt;24&lt;/b&gt;<br>Abierto 26", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://prenotaconme.example/barberia", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Barberia <Demo>", email.TextBody, StringComparison.Ordinal);
    }
}
