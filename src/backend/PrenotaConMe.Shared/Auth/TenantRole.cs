namespace PrenotaConMe.Shared.Auth;

/// <summary>Role of a user inside one tenant (business). A user may hold several roles in the same tenant.</summary>
public enum TenantRole
{
    Admin = 1,
    Staff = 2,
    Client = 3,
}
