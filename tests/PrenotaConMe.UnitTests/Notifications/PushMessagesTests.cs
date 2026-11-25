using System.Text.Json;
using PrenotaConMe.Modules.Notifications;
using PrenotaConMe.Modules.Notifications.Push;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.UnitTests.Notifications;

public sealed class PushMessagesTests
{
    [Fact]
    public void Client_push_links_to_my_appointments_in_local_time()
    {
        var push = PushMessages.Build(AppointmentChange.Booked, AppointmentEmailsTests.Notice(), PushAudience.Client);

        Assert.Equal("Prenotazione confermata", push.Title);
        Assert.Equal("Taglio capelli · mer 2 dic, 10:30 · Barberia <Demo>", push.Body);
        Assert.Equal("/barberia-demo/prenotazioni", push.Url);
    }

    [Fact]
    public void Reminder_push_and_email_name_the_time_and_the_staff_member()
    {
        var push = PushMessages.BuildReminder(AppointmentEmailsTests.Notice());
        var email = AppointmentEmails.ForClient(AppointmentEmailsTests.Notice("es"), DateTimeOffset.UnixEpoch, reminder: true)!;

        Assert.Equal("Promemoria appuntamento", push.Title);
        Assert.Equal("Taglio capelli · mer 2 dic, 10:30 · Mario · Barberia <Demo>", push.Body);
        Assert.Equal("/barberia-demo/prenotazioni", push.Url);
        Assert.StartsWith("Recordatorio: Taglio capelli, ", email.Subject, StringComparison.Ordinal);
        Assert.Contains("te recordamos tu cita en Barberia <Demo>", email.TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AppointmentChange.Booked, "en", "New booking")]
    [InlineData(AppointmentChange.Rescheduled, "es", "Cita cambiada")]
    [InlineData(AppointmentChange.Cancelled, "it", "Appuntamento annullato")]
    public void Staff_push_names_the_client_and_opens_the_calendar(AppointmentChange change, string language, string title)
    {
        var push = PushMessages.Build(change, AppointmentEmailsTests.Notice(language), PushAudience.Staff);

        Assert.Equal(title, push.Title);
        Assert.StartsWith("Anna · Taglio capelli · ", push.Body, StringComparison.Ordinal);
        Assert.Equal("/admin/calendario", push.Url);
    }

    [Fact]
    public void Payload_uses_the_angular_service_worker_format()
    {
        var push = new PushNotification("T", "B", "/x/prenotazioni", "appointment-1");

        using var json = JsonDocument.Parse(push.ToPayload());
        var notification = json.RootElement.GetProperty("notification");

        Assert.Equal("T", notification.GetProperty("title").GetString());
        Assert.Equal("appointment-1", notification.GetProperty("tag").GetString());
        var click = notification.GetProperty("data").GetProperty("onActionClick").GetProperty("default");
        Assert.Equal("navigateLastFocusedOrOpen", click.GetProperty("operation").GetString());
        Assert.Equal("/x/prenotazioni", click.GetProperty("url").GetString());
    }
}
