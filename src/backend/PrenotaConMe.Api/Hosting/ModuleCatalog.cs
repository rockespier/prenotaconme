using PrenotaConMe.Modules.Tenancy;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Modules;

namespace PrenotaConMe.Api.Hosting;

/// <summary>Explicit list of business modules. Add each new module here.</summary>
internal static class ModuleCatalog
{
    public static readonly IModule[] All =
    [
        new TenancyModule(),
        new UsersModule(),
    ];
}
