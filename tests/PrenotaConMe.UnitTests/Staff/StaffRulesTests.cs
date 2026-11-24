using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Shared.Scheduling;

namespace PrenotaConMe.UnitTests.Staff;

public sealed class StaffRulesTests
{
    private static readonly Guid LocationA = Guid.CreateVersion7();
    private static readonly Guid LocationB = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, IReadOnlyList<WeeklyInterval>> Opening = new()
    {
        [LocationA] = [new(DayOfWeek.Tuesday, new(9, 0), new(19, 0))],
        [LocationB] = [new(DayOfWeek.Tuesday, new(9, 0), new(19, 0))],
    };

    private static WorkingHoursDto H(Guid loc, int start, int end, DayOfWeek day = DayOfWeek.Tuesday) =>
        new(loc, day, new TimeOnly(start, 0), new TimeOnly(end, 0));

    private static string? FirstError(params WorkingHoursDto[] hours) =>
        StaffRules.ValidateWorkingHours(hours, new HashSet<Guid> { LocationA, LocationB }, Opening).IsValid
            ? null
            : StaffRules.ValidateWorkingHours(hours, new HashSet<Guid> { LocationA, LocationB }, Opening).ToProblem()
                .ProblemDetails.Errors["workingHours"][0];

    [Fact]
    public void Split_shift_at_two_locations_is_valid() =>
        Assert.Null(FirstError(H(LocationA, 9, 13), H(LocationB, 14, 19)));

    [Fact]
    public void Overlap_across_locations_is_rejected() =>
        Assert.Equal("workingHours.interval.overlap", FirstError(H(LocationA, 9, 13), H(LocationB, 12, 15)));

    [Fact]
    public void Outside_opening_hours_is_rejected() =>
        Assert.Equal("workingHours.outsideOpeningHours", FirstError(H(LocationA, 8, 12)));

    [Fact]
    public void Day_when_location_is_closed_is_rejected() =>
        Assert.Equal("workingHours.outsideOpeningHours", FirstError(H(LocationA, 10, 12, DayOfWeek.Monday)));

    [Fact]
    public void Location_not_assigned_is_rejected() =>
        Assert.Equal("workingHours.locationNotAssigned", FirstError(H(Guid.CreateVersion7(), 10, 12)));

    [Theory]
    [InlineData("Mario@Example.com ", "mario@example.com")]
    public void NormalizeEmail(string input, string expected) => Assert.Equal(expected, StaffRules.NormalizeEmail(input));

    [Theory]
    [InlineData("mario@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("Mario <mario@example.com>", false)]
    public void IsValidEmail(string email, bool expected) => Assert.Equal(expected, StaffRules.IsValidEmail(email));

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(45, false)]
    [InlineData(0, false)]
    [InlineData(510, false)]
    public void Service_duration_must_be_multiple_of_30(int minutes, bool valid)
    {
        var request = new SaveServiceRequest("Taglio", null, minutes, 10m, false, []);
        Assert.Equal(valid, ServiceRules.Validate(request).IsValid);
    }
}
