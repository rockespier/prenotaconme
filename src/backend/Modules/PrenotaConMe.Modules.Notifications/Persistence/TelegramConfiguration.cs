using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Users.Domain;

namespace PrenotaConMe.Modules.Notifications.Persistence;

internal sealed class TelegramBotConfiguration : IEntityTypeConfiguration<TelegramBot>
{
    public void Configure(EntityTypeBuilder<TelegramBot> builder)
    {
        builder.ToTable("telegram_bots");
        builder.Property(b => b.Username).HasMaxLength(64).IsRequired();
        builder.Property(b => b.ProtectedToken).HasMaxLength(1024).IsRequired();
        builder.Property(b => b.WebhookSecret).HasMaxLength(64).IsRequired();
        builder.HasIndex(b => b.TenantId).IsUnique().HasDatabaseName("ix_telegram_bots_tenant_unique");
    }
}

internal sealed class TelegramLinkConfiguration : IEntityTypeConfiguration<TelegramLink>
{
    public void Configure(EntityTypeBuilder<TelegramLink> builder)
    {
        builder.ToTable("telegram_links");
        builder.HasIndex(l => new { l.TenantId, l.UserId }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.ChatId }).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TelegramLinkTokenConfiguration : IEntityTypeConfiguration<TelegramLinkToken>
{
    public void Configure(EntityTypeBuilder<TelegramLinkToken> builder)
    {
        builder.ToTable("telegram_link_tokens");
        builder.Property(t => t.Token).HasMaxLength(TelegramLinkToken.TokenLength).IsRequired();
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
