using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrenotaConMe.Modules.Catalog.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Staff.Domain;

namespace PrenotaConMe.Modules.Staff.Persistence;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff_members");
        builder.Property(s => s.DisplayName).HasMaxLength(StaffRules.NameMaxLength).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Email }).IsUnique();
        builder.HasIndex(s => s.Email); // invitation lookup across businesses
        builder.HasIndex(s => s.UserId);

        builder.HasMany(s => s.Locations).WithOne().HasForeignKey(l => l.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Services).WithOne().HasForeignKey(l => l.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.WorkingHours).WithOne().HasForeignKey(h => h.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.TimeOff).WithOne().HasForeignKey(t => t.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffLocationConfiguration : IEntityTypeConfiguration<StaffLocation>
{
    public void Configure(EntityTypeBuilder<StaffLocation> builder)
    {
        builder.ToTable("staff_locations");
        builder.HasIndex(l => new { l.StaffMemberId, l.LocationId }).IsUnique();
        builder.HasOne<Location>().WithMany().HasForeignKey(l => l.LocationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffServiceConfiguration : IEntityTypeConfiguration<StaffService>
{
    public void Configure(EntityTypeBuilder<StaffService> builder)
    {
        builder.ToTable("staff_services");
        builder.HasIndex(s => new { s.StaffMemberId, s.ServiceId }).IsUnique();
        builder.HasOne<Service>().WithMany().HasForeignKey(s => s.ServiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffWorkingHoursConfiguration : IEntityTypeConfiguration<StaffWorkingHours>
{
    public void Configure(EntityTypeBuilder<StaffWorkingHours> builder)
    {
        builder.ToTable("staff_working_hours");
        builder.HasIndex(h => new { h.StaffMemberId, h.Day });
        builder.HasOne<Location>().WithMany().HasForeignKey(h => h.LocationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffTimeOffConfiguration : IEntityTypeConfiguration<StaffTimeOff>
{
    public void Configure(EntityTypeBuilder<StaffTimeOff> builder)
    {
        builder.ToTable("staff_time_off");
        builder.Property(t => t.Reason).HasMaxLength(200);
        builder.HasIndex(t => new { t.StaffMemberId, t.From });
    }
}
