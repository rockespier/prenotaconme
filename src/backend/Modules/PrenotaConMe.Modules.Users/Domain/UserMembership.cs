using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Users.Domain;

/// <summary>Grants a user a role inside one tenant.</summary>
public sealed class UserMembership : TenantEntity
{
    private UserMembership()
    {
    }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }

    public static UserMembership Create(Guid tenantId, Guid userId, TenantRole role) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        Role = role,
    };
}
