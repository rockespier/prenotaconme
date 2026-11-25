using System.Globalization;
using System.Text.Json;
using PrenotaConMe.Shared.Notifications;

namespace PrenotaConMe.Modules.Notifications.Push;

public enum PushAudience
{
    Client,
    Staff,
}

/// <param name="Url">Page opened when the notification is tapped (relative to the site root).</param>
/// <param name="Tag">Same tag = the newer notification replaces the older one on the device.</param>
public sealed record PushNotification(string Title, string Body, string Url, string Tag)
{
    /// <summary>Payload in the format the Angular service worker (ngsw) shows by itself.</summary>
    public string ToPayload() => JsonSerializer.Serialize(new
    {
        notification = new
        {
            title = Title,
            body = Body,
            icon = "icons/icon-192x192.png",
            badge = "icons/icon-72x72.png",
            tag = Tag,
            renotify = true,
            data = new { onActionClick = new { @default = new { operation = "navigateLastFocusedOrOpen", url = Url } } },
        },
    });
}

/// <summary>F2-02: push texts, in the business's default language like the e-mails.</summary>
internal static class PushMessages
{
    private sealed record Texts(
        string Culture, string ClientBooked, string ClientMoved, string ClientCancelled,
        string StaffBooked, string StaffMoved, string StaffCancelled, string ClientReminder);

    private static readonly Dictionary<string, Texts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new("it-IT", "Prenotazione confermata", "Appuntamento spostato", "Appuntamento annullato",
            "Nuova prenotazione", "Appuntamento spostato", "Appuntamento annullato", "Promemoria appuntamento"),
        ["es"] = new("es-ES", "Reserva confirmada", "Cita cambiada", "Cita cancelada",
            "Nueva reserva", "Cita cambiada", "Cita cancelada", "Recordatorio de tu cita"),
        ["en"] = new("en-GB", "Booking confirmed", "Appointment moved", "Appointment cancelled",
            "New booking", "Appointment moved", "Appointment cancelled", "Appointment reminder"),
    };

    /// <summary>F2-06: reminder to the client of an upcoming appointment.</summary>
    public static PushNotification BuildReminder(AppointmentNotice n)
    {
        var (t, when) = Prepare(n);
        var body = string.Join(" · ", new[] { n.ServiceName, when, n.StaffName, n.BusinessName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new PushNotification(t.ClientReminder, body, $"/{n.BusinessSlug}/prenotazioni", $"appointment-{n.AppointmentId}");
    }

    public static PushNotification Build(AppointmentChange change, AppointmentNotice n, PushAudience audience)
    {
        var (t, when) = Prepare(n);

        string title;
        string[] parts;
        string url;
        if (audience == PushAudience.Client)
        {
            title = change switch
            {
                AppointmentChange.Booked => t.ClientBooked,
                AppointmentChange.Rescheduled => t.ClientMoved,
                _ => t.ClientCancelled,
            };
            parts = [n.ServiceName, when, n.BusinessName];
            url = $"/{n.BusinessSlug}/prenotazioni";
        }
        else
        {
            title = change switch
            {
                AppointmentChange.Booked => t.StaffBooked,
                AppointmentChange.Rescheduled => t.StaffMoved,
                _ => t.StaffCancelled,
            };
            parts = [n.ClientName ?? string.Empty, n.ServiceName, when];
            url = "/admin/calendario";
        }

        var body = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new PushNotification(title, body, url, $"appointment-{n.AppointmentId}");
    }

    private static (Texts T, string When) Prepare(AppointmentNotice n)
    {
        var t = ByLanguage.GetValueOrDefault(n.Language) ?? ByLanguage["it"];
        var culture = CultureInfo.GetCultureInfo(t.Culture);
        var local = TimeZoneInfo.ConvertTime(n.StartsAt, TimeZoneInfo.FindSystemTimeZoneById(n.TimeZone));
        return (t, local.ToString("ddd d MMM, HH:mm", culture));
    }
}
