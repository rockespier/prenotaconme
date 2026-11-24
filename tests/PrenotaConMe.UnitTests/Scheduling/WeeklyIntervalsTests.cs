using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.UnitTests.Scheduling;

public sealed class WeeklyIntervalsTests
{
    private static WeeklyInterval I(DayOfWeek d, int sh, int sm, int eh, int em) => new(d, new TimeOnly(sh, sm), new TimeOnly(eh, em));

    [Fact]
    public void Valid_intervals_pass() =>
        Assert.Null(WeeklyIntervals.Validate([I(DayOfWeek.Monday, 9, 0, 13, 0), I(DayOfWeek.Monday, 14, 0, 19, 0)], 60));

    [Fact]
    public void Touching_intervals_do_not_overlap() =>
        Assert.Null(WeeklyIntervals.Validate([I(DayOfWeek.Monday, 9, 0, 13, 0), I(DayOfWeek.Monday, 13, 0, 14, 0)], 60));

    [Fact]
    public void Start_after_end_is_rejected() =>
        Assert.Equal("interval.order", WeeklyIntervals.Validate([I(DayOfWeek.Monday, 13, 0, 9, 0)], 60));

    [Fact]
    public void Misaligned_time_is_rejected() =>
        Assert.Equal("interval.granularity", WeeklyIntervals.Validate([I(DayOfWeek.Monday, 9, 30, 13, 0)], 60));

    [Fact]
    public void Half_hours_are_fine_with_30_minute_granularity() =>
        Assert.Null(WeeklyIntervals.Validate([I(DayOfWeek.Monday, 9, 30, 13, 0)], 30));

    [Fact]
    public void Overlap_on_same_day_is_rejected() =>
        Assert.Equal("interval.overlap", WeeklyIntervals.Validate([I(DayOfWeek.Friday, 9, 0, 13, 0), I(DayOfWeek.Friday, 12, 0, 15, 0)], 60));

    [Fact]
    public void Same_hours_on_different_days_do_not_overlap() =>
        Assert.Null(WeeklyIntervals.Validate([I(DayOfWeek.Monday, 9, 0, 13, 0), I(DayOfWeek.Tuesday, 9, 0, 13, 0)], 60));

    [Fact]
    public void IsCovered_uses_union_of_touching_blocks()
    {
        WeeklyInterval[] opening = [I(DayOfWeek.Monday, 9, 0, 12, 0), I(DayOfWeek.Monday, 12, 0, 19, 0)];

        Assert.True(WeeklyIntervals.IsCovered(opening, I(DayOfWeek.Monday, 11, 0, 14, 0)));
        Assert.False(WeeklyIntervals.IsCovered(opening, I(DayOfWeek.Monday, 18, 0, 20, 0)));
        Assert.False(WeeklyIntervals.IsCovered(opening, I(DayOfWeek.Tuesday, 10, 0, 11, 0)));
    }

    [Fact]
    public void IsCovered_rejects_gap_between_blocks()
    {
        WeeklyInterval[] opening = [I(DayOfWeek.Monday, 9, 0, 13, 0), I(DayOfWeek.Monday, 15, 0, 19, 0)];

        Assert.False(WeeklyIntervals.IsCovered(opening, I(DayOfWeek.Monday, 12, 0, 16, 0)));
    }
}
