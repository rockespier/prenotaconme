using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Calendar.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;

namespace PrenotaConMe.Modules.Calendar.Persistence;

internal sealed class GoogleCalendarConnectionConfiguration : IEntityTypeConfiguration<GoogleCalendarConnection>
{
    public void Configure(EntityTypeBuilder<GoogleCalendarConnection> builder)
    {
        builder.ToTable("google_calendar_connections");
        builder.Property(c => c.GoogleEmail).HasMaxLength(GoogleCalendarConnection.EmailMaxLength);
        builder.Property(c => c.ProtectedRefreshToken).IsRequired();
        builder.Property(c => c.CalendarId).HasMaxLength(GoogleCalendarConnection.CalendarIdMaxLength).IsRequired();
        builder.Property(c => c.Error).HasMaxLength(GoogleCalendarConnection.ErrorMaxLength);
        builder.HasIndex(c => c.StaffMemberId).IsUnique();
        // Deleting the staff member deletes the connection (nothing else references it).
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(c => c.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CalendarEventLinkConfiguration : IEntityTypeConfiguration<CalendarEventLink>
{
    public void Configure(EntityTypeBuilder<CalendarEventLink> builder)
    {
        builder.ToTable("calendar_event_links");
        builder.Property(l => l.EventId).HasMaxLength(CalendarEventLink.EventIdMaxLength).IsRequired();
        builder.HasIndex(l => l.AppointmentId).IsUnique();
        builder.HasOne<Appointment>().WithMany().HasForeignKey(l => l.AppointmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
