using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Locations.Domain;

namespace PrenotaConMe.Modules.Locations.Persistence;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");
        builder.Property(l => l.Name).HasMaxLength(LocationRules.NameMaxLength).IsRequired();
        builder.Property(l => l.Address).HasMaxLength(200);
        builder.Property(l => l.City).HasMaxLength(100);
        builder.Property(l => l.Phone).HasMaxLength(32);
        builder.Property(l => l.TimeZone).HasMaxLength(64).IsRequired();
        builder.Ignore(l => l.OpeningIntervals);
        builder.OwnsMany(l => l.OpeningHours, oh => oh.ToJson("opening_hours"));
    }
}
