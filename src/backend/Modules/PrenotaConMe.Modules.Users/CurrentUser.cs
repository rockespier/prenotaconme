using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Users.Domain;
using PrenotaConMe.Shared.Auth;

namespace PrenotaConMe.Modules.Users;

/// <summary>
/// Reads the Firebase identity from the validated JWT and maps it to a local <see cref="UserAccount"/>,
/// creating it on first use. Claims: "sub" (uid), "email", "name", "picture".
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, AppDbContext db) : ICurrentUser
{
    private Guid? _userId;

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && ExternalId is not null;

    public string? ExternalId => Principal?.FindFirstValue("sub");

    public string? Email => Principal?.FindFirstValue("email");

    public bool EmailVerified => string.Equals(Principal?.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

    private string? DisplayName => Principal?.FindFirstValue("name");

    private string? PhotoUrl => Principal?.FindFirstValue("picture");

    public async Task<Guid> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_userId is { } cached)
        {
            return cached;
        }

        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("No authenticated user in this request.");
        }

        var user = await FindAsync(cancellationToken);
        if (user is null)
        {
            user = UserAccount.Create(ExternalId!, Email, DisplayName, PhotoUrl);
            db.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent first request created it; use that one.
                db.Entry(user).State = EntityState.Detached;
                user = await FindAsync(cancellationToken)
                    ?? throw new InvalidOperationException("User vanished after unique violation.", ex);
            }
        }
        else if (user.SyncProfile(Email, DisplayName, PhotoUrl))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        _userId = user.Id;
        return user.Id;
    }

    private Task<UserAccount?> FindAsync(CancellationToken cancellationToken) =>
        db.Set<UserAccount>().SingleOrDefaultAsync(u => u.ExternalId == ExternalId, cancellationToken);
}
