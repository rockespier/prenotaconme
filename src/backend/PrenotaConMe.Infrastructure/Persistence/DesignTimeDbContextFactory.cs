using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Infrastructure.Persistence;

/// <summary>Used by dotnet-ef. Connection string from PRENOTA_DB or the local dev default.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PRENOTA_DB")
            ?? "Host=localhost;Port=5432;Database=prenotaconme;Username=prenota;Password=prenota";

        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureNpgsql(options, connectionString);

        return new AppDbContext(options.Options, new TenantContext(), []);
    }
}
