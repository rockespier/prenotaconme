namespace PrenotaConMe.Shared.Privacy;

/// <summary>
/// F5-04 (GDPR): the personal data a module keeps about a user, across every business. The Users module calls all
/// of them for "download my data" (art. 15/20) and "delete my account" (art. 17), so it never queries other modules.
/// Implementations run without a tenant: they must use <c>IgnoreQueryFilters([AppDbContext.TenantFilter])</c>.
/// </summary>
public interface IPersonalData
{
    /// <summary>Name of the section in the export (e.g. "appointments").</summary>
    string Section { get; }

    /// <summary>JSON-serializable data, or null when the module has nothing about the user.</summary>
    Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes or anonymizes the user's data that would survive the deletion of the account (rows with a foreign
    /// key to it are deleted by cascade). Only stages changes; the caller saves once.
    /// </summary>
    Task EraseAsync(Guid userId, CancellationToken cancellationToken);
}
