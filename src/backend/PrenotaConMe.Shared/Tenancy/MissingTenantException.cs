namespace PrenotaConMe.Shared.Tenancy;

public sealed class MissingTenantException()
    : InvalidOperationException("A tenant-owned entity was saved without a tenant in the current scope.");
