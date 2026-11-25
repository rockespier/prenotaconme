using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Modules.Users.Domain;

namespace PrenotaConMe.Modules.Notifications.Persistence;

internal sealed class ReminderPreferenceConfiguration : IEntityTypeConfiguration<ReminderPreference>
{
    public void Configure(EntityTypeBuilder<ReminderPreference> builder)
    {
        builder.ToTable("reminder_preferences");
        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
