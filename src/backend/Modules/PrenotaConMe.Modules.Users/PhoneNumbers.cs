using System.Text.RegularExpressions;

namespace PrenotaConMe.Modules.Users;

public static partial class PhoneNumbers
{
    /// <summary>
    /// International format required (E.164, e.g. "+39 333 123 4567" becomes "+393331234567"): reminders over
    /// WhatsApp/Telegram need the country code. Spaces, dots, dashes and parentheses are ignored. Null when invalid.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var compact = Separators().Replace(input ?? string.Empty, string.Empty);
        if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = "+" + compact[2..];
        }

        return E164().IsMatch(compact) ? compact : null;
    }

    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164();
}
