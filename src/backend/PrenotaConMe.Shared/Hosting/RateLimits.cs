namespace PrenotaConMe.Shared.Hosting;

/// <summary>F5-05: rate limiting policies registered by the Api host.</summary>
public static class RateLimits
{
    /// <summary>
    /// Endpoints that send e-mails or messages, call other services or create businesses: a few per hour per user
    /// (<c>RateLimiting:SensitivePerHour</c>). Every /api call also counts against the per-minute limit.
    /// </summary>
    public const string Sensitive = "sensitive";
}
