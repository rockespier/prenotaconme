using PrenotaConMe.Shared.Domain;

namespace PrenotaConMe.Modules.Staff.Domain;

/// <summary>
/// A person who performs services (barber, hairdresser, beautician). Created by an Admin with an e-mail;
/// linked to a user account (and granted the Staff role) when someone signs in with that verified e-mail.
/// </summary>
public sealed class StaffMember : TenantEntity
{
    private StaffMember()
    {
    }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Lowercased; unique per business.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Local user id once the invitation is accepted (signed in with <see cref="Email"/>).</summary>
    public Guid? UserId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public List<StaffLocation> Locations { get; private set; } = [];

    public List<StaffService> Services { get; private set; } = [];

    /// <summary>Weekly availability per location, in whole hours (F1-07).</summary>
    public List<StaffWorkingHours> WorkingHours { get; private set; } = [];

    public List<StaffTimeOff> TimeOff { get; private set; } = [];

    public static StaffMember Create(StaffData data)
    {
        var staff = new StaffMember { Email = data.Email };
        staff.Update(data);
        return staff;
    }

    public void Update(StaffData data)
    {
        DisplayName = data.DisplayName.Trim();
        IsActive = data.IsActive;

        var locations = data.LocationIds.ToHashSet();
        Locations.RemoveAll(l => !locations.Contains(l.LocationId));
        Locations.AddRange(locations.Where(id => Locations.All(l => l.LocationId != id)).Select(id => new StaffLocation(Id, id)));

        // Hours at a location the person no longer works at are meaningless.
        WorkingHours.RemoveAll(h => !locations.Contains(h.LocationId));

        var services = data.ServiceIds.ToHashSet();
        Services.RemoveAll(s => !services.Contains(s.ServiceId));
        Services.AddRange(services.Where(id => Services.All(s => s.ServiceId != id)).Select(id => new StaffService(Id, id)));
    }

    /// <summary>E-mail can change only while the invitation is pending.</summary>
    public bool TryChangeEmail(string email)
    {
        if (UserId is not null && email != Email)
        {
            return false;
        }

        Email = email;
        return true;
    }

    public void LinkUser(Guid userId) => UserId = userId;

    public void ReplaceWorkingHours(IEnumerable<StaffWorkingHours> hours)
    {
        WorkingHours.Clear();
        WorkingHours.AddRange(hours);
    }
}

public sealed class StaffLocation(Guid staffMemberId, Guid locationId) : TenantEntity
{
    public Guid StaffMemberId { get; private set; } = staffMemberId;

    public Guid LocationId { get; private set; } = locationId;
}

public sealed class StaffService(Guid staffMemberId, Guid serviceId) : TenantEntity
{
    public Guid StaffMemberId { get; private set; } = staffMemberId;

    public Guid ServiceId { get; private set; } = serviceId;
}

/// <summary>A working block on a weekday at a location, local time, whole hours ("franjas de 1 hora").</summary>
public sealed class StaffWorkingHours(Guid staffMemberId, Guid locationId, DayOfWeek day, TimeOnly start, TimeOnly end) : TenantEntity
{
    public Guid StaffMemberId { get; private set; } = staffMemberId;

    public Guid LocationId { get; private set; } = locationId;

    public DayOfWeek Day { get; private set; } = day;

    public TimeOnly Start { get; private set; } = start;

    public TimeOnly End { get; private set; } = end;
}

/// <summary>Full days off (holidays, sick leave), inclusive range.</summary>
public sealed class StaffTimeOff(Guid staffMemberId, DateOnly from, DateOnly to, string? reason) : TenantEntity
{
    public Guid StaffMemberId { get; private set; } = staffMemberId;

    public DateOnly From { get; private set; } = from;

    public DateOnly To { get; private set; } = to;

    public string? Reason { get; private set; } = reason;
}

public sealed record StaffData(
    string DisplayName, string Email, bool IsActive, IReadOnlyCollection<Guid> LocationIds, IReadOnlyCollection<Guid> ServiceIds);
