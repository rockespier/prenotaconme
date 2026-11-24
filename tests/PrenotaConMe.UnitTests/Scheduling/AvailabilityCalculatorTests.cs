using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.UnitTests.Scheduling;

public sealed class AvailabilityCalculatorTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly Day = new(2030, 12, 3); // winter: Rome = UTC+1
    private static readonly DateTimeOffset LongAgo = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Mario = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Luigi = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static WeeklyInterval On(DateOnly day, int startHour, int endHour) =>
        new(day.DayOfWeek, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private static StaffAvailabilityInput Staff(
        Guid id, IReadOnlyList<WeeklyInterval> hours,
        IReadOnlyList<(DateOnly, DateOnly)>? timeOff = null,
        IReadOnlyList<(DateTimeOffset, DateTimeOffset)>? busy = null) =>
        new(id, hours, timeOff ?? [], busy ?? []);

    private static AvailabilityInput Input(int duration, params StaffAvailabilityInput[] staff) =>
        new(Rome, [On(Day, 8, 20)], duration, Day, Day, LongAgo, staff);

    private static DateTimeOffset RomeTime(int hour, int minute = 0) =>
        new(Day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(1));

    private static string[] Times(IEnumerable<AvailableSlot> slots) =>
        slots.Select(s => s.LocalTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    [Fact]
    public void Thirty_minute_service_fills_the_working_block_on_the_grid()
    {
        var slots = AvailabilityCalculator.Calculate(Input(30, Staff(Mario, [On(Day, 9, 12)])));

        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00", "11:30"], Times(slots));
        Assert.Equal(RomeTime(9), slots[0].StartsAt);
        Assert.Equal(TimeSpan.Zero, slots[0].StartsAt.Offset); // returned as UTC
    }

    [Fact]
    public void Longer_service_must_fit_before_the_block_ends()
    {
        var slots = AvailabilityCalculator.Calculate(Input(60, Staff(Mario, [On(Day, 9, 12)])));

        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00"], Times(slots));
    }

    [Fact]
    public void Adjacent_working_blocks_are_merged()
    {
        var slots = AvailabilityCalculator.Calculate(Input(60, Staff(Mario, [On(Day, 9, 10), On(Day, 10, 11)])));

        Assert.Equal(["09:00", "09:30", "10:00"], Times(slots));
    }

    [Fact]
    public void Existing_appointments_block_overlapping_starts_but_allow_back_to_back()
    {
        var busy = (RomeTime(10), RomeTime(10, 30));
        var slots = AvailabilityCalculator.Calculate(Input(60, Staff(Mario, [On(Day, 9, 12)], busy: [busy])));

        // 09:30 and 10:00 overlap the 10:00–10:30 appointment; 09:00 ends exactly at 10:00 and 10:30 starts at its end.
        Assert.Equal(["09:00", "10:30", "11:00"], Times(slots));
    }

    [Fact]
    public void Day_off_has_no_slots()
    {
        var slots = AvailabilityCalculator.Calculate(Input(30, Staff(Mario, [On(Day, 9, 12)], timeOff: [(Day.AddDays(-1), Day)])));

        Assert.Empty(slots);
    }

    [Fact]
    public void Slots_outside_opening_hours_are_not_offered()
    {
        var input = Input(60, Staff(Mario, [On(Day, 9, 13)])) with { OpeningHours = [On(Day, 9, 11)] };

        Assert.Equal(["09:00", "09:30", "10:00"], Times(AvailabilityCalculator.Calculate(input)));
    }

    [Fact]
    public void Past_slots_are_dropped()
    {
        var input = Input(30, Staff(Mario, [On(Day, 9, 12)])) with { Now = RomeTime(10, 15) };

        Assert.Equal(["10:30", "11:00", "11:30"], Times(AvailabilityCalculator.Calculate(input)));
    }

    [Fact]
    public void Slot_lists_every_free_staff_member()
    {
        var slots = AvailabilityCalculator.Calculate(Input(30,
            Staff(Mario, [On(Day, 9, 10)]),
            Staff(Luigi, [On(Day, 9, 11)], busy: [(RomeTime(9), RomeTime(9, 30))])));

        Assert.Equal([Mario], slots.Single(s => s.LocalTime == new TimeOnly(9, 0)).StaffIds);
        Assert.Equal([Mario, Luigi], slots.Single(s => s.LocalTime == new TimeOnly(9, 30)).StaffIds);
        Assert.Equal([Luigi], slots.Single(s => s.LocalTime == new TimeOnly(10, 0)).StaffIds);
    }

    [Fact]
    public void Times_skipped_by_daylight_saving_are_not_offered()
    {
        var dstDay = new DateOnly(2030, 3, 31); // Rome: 02:00 → 03:00
        var input = new AvailabilityInput(Rome, [On(dstDay, 0, 23)], 30, dstDay, dstDay, LongAgo,
            [Staff(Mario, [On(dstDay, 1, 4)])]);

        var times = Times(AvailabilityCalculator.Calculate(input));

        Assert.Equal(["01:00", "01:30", "03:00", "03:30"], times);
    }
}
