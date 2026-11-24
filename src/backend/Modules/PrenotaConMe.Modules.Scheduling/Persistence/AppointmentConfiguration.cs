using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;

namespace PrenotaConMe.Modules.Scheduling.Persistence;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    /// <summary>Created in the migration with raw SQL (EF does not model exclusion constraints).</summary>
    public const string NoOverlapConstraint = "ex_appointments_staff_no_overlap";

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", t =>
        {
            t.HasCheckConstraint("ck_appointments_ends_after_start", "ends_at > starts_at");
            t.HasCheckConstraint("ck_appointments_has_client", "client_user_id IS NOT NULL OR client_name IS NOT NULL");
        });
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.ServiceName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Price).HasPrecision(10, 2);
        builder.Property(a => a.ClientName).HasMaxLength(Appointment.ClientNameMaxLength);
        builder.Property(a => a.ClientPhone).HasMaxLength(Appointment.ClientPhoneMaxLength);

        builder.HasIndex(a => new { a.StaffMemberId, a.StartsAt });
        builder.HasIndex(a => new { a.LocationId, a.StartsAt });
        builder.HasIndex(a => a.ClientUserId);

        // History must survive: deleting a location/service/staff member that has appointments is refused.
        builder.HasOne<Location>().WithMany().HasForeignKey(a => a.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(a => a.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
    }
}
