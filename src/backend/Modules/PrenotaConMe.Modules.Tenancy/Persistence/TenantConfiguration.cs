using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Tenancy.Domain;

namespace PrenotaConMe.Modules.Tenancy.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Property(t => t.Name).HasMaxLength(TenantRules.NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(TenantRules.SlugMaxLength).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(t => t.DefaultLanguage).HasMaxLength(5).IsRequired();
    }
}
