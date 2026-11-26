using System.Text;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>
/// F3-07: fixed answers to a bare greeting or thanks, without calling the model (zero tokens). Only messages that
/// are nothing else match ("ciao!" does, "ciao, posso prenotare?" does not), answered in the greeting's language.
/// </summary>
internal static class QuickReplies
{
    private static readonly Dictionary<string, string> Greetings = new(StringComparer.Ordinal)
    {
        ["ciao"] = "it", ["salve"] = "it", ["buongiorno"] = "it", ["buonasera"] = "it", ["buon pomeriggio"] = "it",
        ["hola"] = "es", ["buenas"] = "es", ["buenos dias"] = "es", ["buenas tardes"] = "es", ["buenas noches"] = "es",
        ["hi"] = "en", ["hello"] = "en", ["hey"] = "en", ["good morning"] = "en", ["good afternoon"] = "en", ["good evening"] = "en",
    };

    private static readonly Dictionary<string, string> Thanks = new(StringComparer.Ordinal)
    {
        ["grazie"] = "it", ["grazie mille"] = "it", ["ok grazie"] = "it",
        ["gracias"] = "es", ["muchas gracias"] = "es", ["vale gracias"] = "es", ["ok gracias"] = "es",
        ["thanks"] = "en", ["thank you"] = "en", ["thx"] = "en", ["ok thanks"] = "en",
    };

    /// <summary>
    /// F3-06: phrases that ask for a person, matched as whole words in the normalized text. Kept specific on purpose
    /// ("una persona" alone is also "a table for one"): anything vaguer is left to the model's handoff tool.
    /// </summary>
    private static readonly string[] HandoffPhrases =
    [
        "operatore", "operatrice", "un umano", "persona vera", "persona reale", "parlare con qualcuno", "parlare con una persona",
        "humano", "asesor", "persona real", "hablar con alguien", "hablar con una persona",
        "human", "operator", "real person", "talk to someone", "speak to someone", "talk to a person", "speak to a person",
    ];

    /// <summary>F3-06: the client asks for a person (no model call needed).</summary>
    public static bool AsksForPerson(string text)
    {
        var padded = $" {Normalize(text)} ";
        return HandoffPhrases.Any(p => padded.Contains($" {p} ", StringComparison.Ordinal));
    }

    /// <summary>The fixed reply, or null when the message needs the model.</summary>
    public static string? For(string text, string? clientName)
    {
        var key = Normalize(text);
        var name = clientName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (Greetings.TryGetValue(key, out var language))
        {
            return language switch
            {
                "es" => $"¡Hola{Name(name)}! Puedo ayudarte a reservar, cambiar o cancelar una cita, o a ver tus citas. ¿Qué necesitas?",
                "en" => $"Hi{Name(name)}! I can help you book, move or cancel an appointment, or check your appointments. What do you need?",
                _ => $"Ciao{Name(name)}! Posso aiutarti a prenotare, spostare o disdire un appuntamento, o a vedere i tuoi appuntamenti. Cosa ti serve?",
            };
        }

        if (Thanks.TryGetValue(key, out language))
        {
            return language switch
            {
                "es" => "¡De nada! Si necesitas algo más, escríbeme.",
                "en" => "You're welcome! Write to me if you need anything else.",
                _ => "Prego! Se ti serve altro, scrivimi.",
            };
        }

        return null;
    }

    private static string Name(string? name) => string.IsNullOrEmpty(name) ? string.Empty : $" {name}";

    /// <summary>
    /// Lower case without accents, punctuation or emoji, single spaces. Decomposing first (FormD) splits "í" into
    /// "i" + a combining accent, which is not a letter and is dropped.
    /// </summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetter(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }
}
