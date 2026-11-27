using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Tenancy.Domain;

namespace PrenotaConMe.Modules.Tenancy.Persistence;

internal sealed class TenantImageConfiguration : IEntityTypeConfiguration<TenantImage>
{
    public void Configure(EntityTypeBuilder<TenantImage> builder)
    {
        builder.ToTable("tenant_images");
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.ContentType).HasMaxLength(32).IsRequired();
        builder.HasIndex(i => new { i.TenantId, i.Kind }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
