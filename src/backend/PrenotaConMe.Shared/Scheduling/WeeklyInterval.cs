namespace PrenotaConMe.Shared.Scheduling;

/// <summary>A time range on a weekday, in the local time of a location. End is exclusive and after Start (no overnight ranges).</summary>
public readonly record struct WeeklyInterval(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public static class WeeklyIntervals
{
    /// <summary>
    /// Returns an error code or null: "interval.order" (start ≥ end), "interval.granularity" (not aligned to
    /// <paramref name="granularityMinutes"/>), "interval.overlap" (two ranges on the same day overlap).
    /// </summary>
    public static string? Validate(IEnumerable<WeeklyInterval> intervals, int granularityMinutes)
    {
        var list = intervals.ToList();

        if (list.Any(i => i.Start >= i.End))
        {
            return "interval.order";
        }

        if (list.Any(i => !IsAligned(i.Start, granularityMinutes) || !IsAligned(i.End, granularityMinutes)))
        {
            return "interval.granularity";
        }

        foreach (var day in list.GroupBy(i => i.Day))
        {
            var sorted = day.OrderBy(i => i.Start).ToList();
            for (var k = 1; k < sorted.Count; k++)
            {
                if (sorted[k].Start < sorted[k - 1].End)
                {
                    return "interval.overlap";
                }
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="inner"/> lies entirely within the union of <paramref name="outer"/> ranges of that day.</summary>
    public static bool IsCovered(IEnumerable<WeeklyInterval> outer, WeeklyInterval inner)
    {
        foreach (var block in Merge(outer.Where(o => o.Day == inner.Day)))
        {
            if (block.Start <= inner.Start && inner.End <= block.End)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Merges overlapping or touching ranges of the same day (input must be a single day).</summary>
    public static IEnumerable<WeeklyInterval> Merge(IEnumerable<WeeklyInterval> sameDay)
    {
        WeeklyInterval? current = null;
        foreach (var next in sameDay.OrderBy(i => i.Start))
        {
            if (current is { } c && next.Start <= c.End)
            {
                current = c with { End = next.End > c.End ? next.End : c.End };
                continue;
            }

            if (current is { } done)
            {
                yield return done;
            }

            current = next;
        }

        if (current is { } last)
        {
            yield return last;
        }
    }

    private static bool IsAligned(TimeOnly time, int granularityMinutes) =>
        time.Second == 0 && time.Millisecond == 0 && (time.Hour * 60 + time.Minute) % granularityMinutes == 0;
}
