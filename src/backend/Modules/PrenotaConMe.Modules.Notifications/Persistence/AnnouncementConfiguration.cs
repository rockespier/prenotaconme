using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Notifications.Domain;

namespace PrenotaConMe.Modules.Notifications.Persistence;

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");
        builder.Property(a => a.Title).HasMaxLength(Announcement.TitleMaxLength).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(Announcement.MessageMaxLength).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.EndsOn });
    }
}
