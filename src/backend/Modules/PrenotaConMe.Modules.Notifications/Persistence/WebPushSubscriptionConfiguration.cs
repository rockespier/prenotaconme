using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Users.Domain;

namespace PrenotaConMe.Modules.Notifications.Persistence;

internal sealed class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.ToTable("web_push_subscriptions");
        builder.Property(s => s.Endpoint).HasMaxLength(WebPushSubscription.EndpointMaxLength).IsRequired();
        builder.HasIndex(s => s.Endpoint).IsUnique();
        builder.Property(s => s.P256dh).HasMaxLength(WebPushSubscription.KeyMaxLength).IsRequired();
        builder.Property(s => s.Auth).HasMaxLength(WebPushSubscription.KeyMaxLength).IsRequired();
        builder.HasIndex(s => s.UserId);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
