using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PrenotaConMe.Infrastructure.Persistence;

/// <summary>Recognizes PostgreSQL constraint violations, whether raised by SaveChanges or by ExecuteUpdate/ExecuteDelete.</summary>
public static class DbErrors
{
    public static bool IsUniqueViolation(Exception ex) => Is(ex, PostgresErrorCodes.UniqueViolation);

    /// <summary>A row is still referenced: 23503 (foreign key) or 23001 (ON DELETE RESTRICT).</summary>
    public static bool IsForeignKeyViolation(Exception ex) =>
        Is(ex, PostgresErrorCodes.ForeignKeyViolation) || Is(ex, PostgresErrorCodes.RestrictViolation);

    public static bool IsExclusionViolation(Exception ex) => Is(ex, PostgresErrorCodes.ExclusionViolation);

    /// <summary>
    /// Two writes claimed overlapping times (EXCLUDE constraint). Concurrent inserts that all see each other's
    /// uncommitted rows wait on one another, and PostgreSQL ends that with a deadlock (40P01) instead of 23P01:
    /// for a booking both mean "someone else took the time", never a server error.
    /// </summary>
    public static bool IsOverlapConflict(Exception ex) =>
        IsExclusionViolation(ex) || Is(ex, PostgresErrorCodes.DeadlockDetected);

    private static bool Is(Exception ex, string sqlState) => ex switch
    {
        PostgresException pg => pg.SqlState == sqlState,
        DbUpdateException { InnerException: PostgresException pg } => pg.SqlState == sqlState,
        _ => false,
    };
}
