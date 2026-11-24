using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;

namespace PrenotaConMe.Modules.Catalog.Persistence;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.Property(s => s.Name).HasMaxLength(ServiceRules.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(ServiceRules.DescriptionMaxLength);
        builder.Property(s => s.Price).HasPrecision(10, 2);
        builder.HasMany(s => s.Locations).WithOne().HasForeignKey(l => l.ServiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceLocationConfiguration : IEntityTypeConfiguration<ServiceLocation>
{
    public void Configure(EntityTypeBuilder<ServiceLocation> builder)
    {
        builder.ToTable("service_locations");
        builder.HasIndex(l => new { l.ServiceId, l.LocationId }).IsUnique();
        builder.HasOne<Location>().WithMany().HasForeignKey(l => l.LocationId).OnDelete(DeleteBehavior.Cascade);
    }
}
