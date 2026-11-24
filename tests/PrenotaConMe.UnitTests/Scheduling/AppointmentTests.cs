using PrenotaConMe.Modules.Scheduling.Domain;

namespace PrenotaConMe.UnitTests.Scheduling;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset Start = new(2030, 12, 3, 9, 0, 0, TimeSpan.Zero);

    private static Appointment Book() =>
        Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), "Taglio", 20m, Guid.NewGuid(), Guid.NewGuid(), Start, 45);

    [Theory]
    [InlineData(-121, true)]
    [InlineData(-120, true)]
    [InlineData(-119, false)]
    public void Can_change_until_the_notice_deadline(int minutesFromStart, bool expected) =>
        Assert.Equal(expected, Book().CanChange(Start.AddMinutes(minutesFromStart), 2));

    [Fact]
    public void Cancelled_appointment_cannot_change()
    {
        var appointment = Book();
        appointment.Cancel(Start.AddDays(-1));

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(Start.AddDays(-1), appointment.CancelledAt);
        Assert.False(appointment.CanChange(Start.AddDays(-1), 2));
    }

    [Fact]
    public void Reschedule_keeps_the_duration()
    {
        var appointment = Book();
        var staff = Guid.NewGuid();

        appointment.Reschedule(staff, Start.AddHours(3));

        Assert.Equal(staff, appointment.StaffMemberId);
        Assert.Equal(Start.AddHours(3), appointment.StartsAt);
        Assert.Equal(Start.AddHours(3).AddMinutes(45), appointment.EndsAt);
    }
}
