using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Tenancy.Domain;

/// <summary>F5-08: the pictures a business shows on its booking page.</summary>
public enum TenantImageKind
{
    /// <summary>Square, shown in a round avatar (512×512 after the browser resizes it).</summary>
    Logo,

    /// <summary>Wide banner at the top of the booking page (1600×600 after the browser resizes it).</summary>
    Cover,
}

/// <summary>
/// F5-08: an uploaded image, stored in the database (a few hundred KB per business, survives redeploys and backups
/// with everything else). Not <see cref="TenantEntity"/>: it is served anonymously by slug, like the tenant itself.
/// </summary>
public sealed class TenantImage : Entity
{
    private TenantImage()
    {
    }

    public Guid TenantId { get; private set; }

    public TenantImageKind Kind { get; private set; }

    /// <summary>Detected from the bytes, never taken from the upload.</summary>
    public string ContentType { get; private set; } = string.Empty;

    public byte[] Data { get; private set; } = [];

    public static TenantImage Create(Guid tenantId, TenantImageKind kind) => new() { TenantId = tenantId, Kind = kind };

    public void Replace(string contentType, byte[] data)
    {
        ContentType = contentType;
        Data = data;
    }
}

public static class TenantImageRules
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>PNG, JPEG or WebP by their signature (SVG is refused: it can carry scripts).</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
