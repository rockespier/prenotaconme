namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>
/// Bot replies, in the business's default language. {0} = business name. A link, when there is one, goes on its own
/// line after the text, so every reply reads well without it (Notifications:PublicUrl not set).
/// </summary>
internal sealed record TelegramTexts(string Welcome, string Linked, string InvalidLink, string Unlinked, string NotYet, string LinkFirst)
{
    private static readonly Dictionary<string, TelegramTexts> ByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["it"] = new(
            "Ciao! Sono il bot di {0}. Per ricevere qui conferme e promemoria, collega il tuo account dalla pagina \"Le mie prenotazioni\" sul sito.",
            "✅ Fatto! Riceverai qui conferme, modifiche e promemoria delle tue prenotazioni presso {0}. Scrivi /stop per scollegarti.",
            "Questo link non è più valido. Generane uno nuovo dalla pagina \"Le mie prenotazioni\" sul sito.",
            "Account scollegato: non riceverai più messaggi di {0} qui.",
            "Per ora non posso prendere prenotazioni in chat: qui ricevi le notifiche di {0}. Per prenotare usa il sito.",
            "Per prenotare qui in chat collega prima il tuo account: apri la pagina \"Le mie prenotazioni\" sul sito e tocca \"Collega\" nella sezione Telegram."),
        ["es"] = new(
            "¡Hola! Soy el bot de {0}. Para recibir aquí confirmaciones y recordatorios, vincula tu cuenta desde la página \"Mis citas\" del sitio.",
            "✅ ¡Listo! Recibirás aquí confirmaciones, cambios y recordatorios de tus citas en {0}. Escribe /stop para desvincularte.",
            "Este enlace ya no es válido. Genera uno nuevo desde la página \"Mis citas\" del sitio.",
            "Cuenta desvinculada: ya no recibirás mensajes de {0} aquí.",
            "Por ahora no puedo tomar reservas por chat: aquí recibes los avisos de {0}. Para reservar usa el sitio.",
            "Para reservar aquí por chat vincula primero tu cuenta: abre la página \"Mis citas\" del sitio y toca \"Vincular\" en la sección Telegram."),
        ["en"] = new(
            "Hi! I am the bot of {0}. To get confirmations and reminders here, link your account from the \"My bookings\" page on the website.",
            "✅ Done! You will get confirmations, changes and reminders of your bookings with {0} here. Send /stop to unlink.",
            "This link is no longer valid. Create a new one from the \"My bookings\" page on the website.",
            "Account unlinked: you will no longer get messages from {0} here.",
            "I cannot take bookings in chat yet: here you get the notifications of {0}. To book, use the website.",
            "To book here in the chat, link your account first: open the \"My bookings\" page on the website and tap \"Link\" in the Telegram section."),
    };

    public static TelegramTexts For(string language) => ByLanguage.GetValueOrDefault(language) ?? ByLanguage["it"];

    public static string Format(string text, string businessName, string? link)
    {
        var message = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, businessName);
        return string.IsNullOrWhiteSpace(link) ? message : message + "\n" + link;
    }
}
