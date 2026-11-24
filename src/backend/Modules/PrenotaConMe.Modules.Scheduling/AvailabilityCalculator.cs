using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>What the calculator needs to know about one staff member (already filtered to the location and service).</summary>
public sealed record StaffAvailabilityInput(
    Guid StaffId,
    IReadOnlyList<WeeklyInterval> WorkingHours,
    IReadOnlyList<(DateOnly From, DateOnly To)> TimeOff,
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Busy);

public sealed record AvailabilityInput(
    TimeZoneInfo TimeZone,
    IReadOnlyList<WeeklyInterval> OpeningHours,
    int DurationMinutes,
    DateOnly From,
    DateOnly To,
    DateTimeOffset Now,
    IReadOnlyList<StaffAvailabilityInput> Staff);

/// <summary>A start time offered to clients and the staff members free for the whole service at that time.</summary>
public sealed record AvailableSlot(DateOnly Date, TimeOnly LocalTime, DateTimeOffset StartsAt, IReadOnlyList<Guid> StaffIds);

/// <summary>
/// F1-08 availability engine (pure: no I/O, no clock). Slots start on the 30-minute grid in the location's
/// local time and must fit entirely inside the staff member's working hours and the location's opening hours,
/// outside their days off and without overlapping existing appointments. Slots in the past are dropped.
/// </summary>
public static class AvailabilityCalculator
{
    public const int GridMinutes = 30;

    public static IReadOnlyList<AvailableSlot> Calculate(AvailabilityInput input)
    {
        var slots = new List<AvailableSlot>();
        var duration = TimeSpan.FromMinutes(input.DurationMinutes);

        for (var date = input.From; date <= input.To; date = date.AddDays(1))
        {
            // start time → staff free at that time, kept sorted by local time
            var byStart = new SortedDictionary<TimeOnly, (DateTimeOffset StartsAt, List<Guid> Staff)>();

            foreach (var staff in input.Staff)
            {
                if (staff.TimeOff.Any(t => t.From <= date && date <= t.To))
                {
                    continue;
                }

                var blocks = WeeklyIntervals.Merge(staff.WorkingHours.Where(h => h.Day == date.DayOfWeek));
                foreach (var block in blocks)
                {
                    foreach (var start in GridStarts(block, input.DurationMinutes))
                    {
                        var local = new WeeklyInterval(date.DayOfWeek, start, start.AddMinutes(input.DurationMinutes));
                        if (!WeeklyIntervals.IsCovered(input.OpeningHours, local))
                        {
                            continue;
                        }

                        if (ToUtc(date, start, input.TimeZone) is not { } startsAt || startsAt < input.Now)
                        {
                            continue;
                        }

                        var endsAt = startsAt + duration;
                        if (staff.Busy.Any(b => b.Start < endsAt && startsAt < b.End))
                        {
                            continue;
                        }

                        if (!byStart.TryGetValue(start, out var entry))
                        {
                            entry = (startsAt, []);
                            byStart[start] = entry;
                        }

                        entry.Staff.Add(staff.StaffId);
                    }
                }
            }

            slots.AddRange(byStart.Select(kv => new AvailableSlot(date, kv.Key, kv.Value.StartsAt, kv.Value.Staff)));
        }

        return slots;
    }

    /// <summary>Grid-aligned starts such that the whole service fits in the block (no midnight crossing).</summary>
    private static IEnumerable<TimeOnly> GridStarts(WeeklyInterval block, int durationMinutes)
    {
        var startMinutes = Minutes(block.Start);
        var first = (startMinutes + GridMinutes - 1) / GridMinutes * GridMinutes;
        var end = Minutes(block.End);

        for (var m = first; m + durationMinutes <= end; m += GridMinutes)
        {
            yield return new TimeOnly(m / 60, m % 60);
        }
    }

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>Local wall-clock time → UTC; null for times skipped by a DST jump.</summary>
    internal static DateTimeOffset? ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }
}
