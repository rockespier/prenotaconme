using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Users.Domain;

namespace PrenotaConMe.Modules.Users.Persistence;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.ExternalId).HasMaxLength(128).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.PhotoUrl).HasMaxLength(2048);
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
    }
}

internal sealed class UserMembershipConfiguration : IEntityTypeConfiguration<UserMembership>
{
    public void Configure(EntityTypeBuilder<UserMembership> builder)
    {
        builder.ToTable("user_memberships");
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(m => new { m.TenantId, m.UserId, m.Role }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
