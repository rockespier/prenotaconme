namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>Bot replies, in the business's default language. {0} = business name, {1} = link (may be empty).</summary>
internal sealed record TelegramTexts(string Welcome, string Linked, string InvalidLink, string Unlinked, string NotYet)
{
    private static readonly Dictionary<string, TelegramTexts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new(
            "Ciao! Sono il bot di {0}. Per ricevere qui conferme e promemoria, collega il tuo account dalla pagina \"Le mie prenotazioni\". {1}",
            "✅ Fatto! Riceverai qui conferme, modifiche e promemoria delle tue prenotazioni presso {0}. Scrivi /stop per scollegarti.",
            "Questo link non è più valido. Generane uno nuovo dalla pagina \"Le mie prenotazioni\". {1}",
            "Account scollegato: non riceverai più messaggi di {0} qui.",
            "Per ora qui ricevi le notifiche di {0}. Per prenotare: {1}"),
        ["es"] = new(
            "¡Hola! Soy el bot de {0}. Para recibir aquí confirmaciones y recordatorios, vincula tu cuenta desde la página \"Mis citas\". {1}",
            "✅ ¡Listo! Recibirás aquí confirmaciones, cambios y recordatorios de tus citas en {0}. Escribe /stop para desvincularte.",
            "Este enlace ya no es válido. Genera uno nuevo desde la página \"Mis citas\". {1}",
            "Cuenta desvinculada: ya no recibirás mensajes de {0} aquí.",
            "Por ahora aquí recibes los avisos de {0}. Para reservar: {1}"),
        ["en"] = new(
            "Hi! I am the bot of {0}. To get confirmations and reminders here, link your account from the \"My bookings\" page. {1}",
            "✅ Done! You will get confirmations, changes and reminders of your bookings with {0} here. Send /stop to unlink.",
            "This link is no longer valid. Create a new one from the \"My bookings\" page. {1}",
            "Account unlinked: you will no longer get messages from {0} here.",
            "For now you get the notifications of {0} here. To book: {1}"),
    };

    public static TelegramTexts For(string language) => ByLanguage.GetValueOrDefault(language) ?? ByLanguage["it"];

    public static string Format(string text, string businessName, string? link) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, text, businessName, link ?? string.Empty).TrimEnd();
}
