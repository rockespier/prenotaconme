using Microsoft.EntityFrameworkCore;
using Npgsql;
using PrenotaConMe.Infrastructure.Persistence;

namespace PrenotaConMe.UnitTests.Persistence;

public sealed class DbErrorsTests
{
    private static DbUpdateException Failed(string sqlState) =>
        new("save failed", new PostgresException("error", "ERROR", "ERROR", sqlState));

    [Theory]
    [InlineData(PostgresErrorCodes.ExclusionViolation, true)]
    [InlineData(PostgresErrorCodes.DeadlockDetected, true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, false)]
    public void Overlapping_bookings_are_recognized_also_when_postgres_reports_a_deadlock(string sqlState, bool expected) =>
        Assert.Equal(expected, DbErrors.IsOverlapConflict(Failed(sqlState)));
}
