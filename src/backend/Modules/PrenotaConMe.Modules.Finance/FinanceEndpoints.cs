using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Finance.Domain;
using PrenotaConMe.Modules.Locations.Domain;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;
using PrenotaConMe.Shared.Validation;

namespace PrenotaConMe.Modules.Finance;

/// <summary>F4-02..06: expenses, the monthly report (income, expenses, net, staff, services) and its PDF/Excel export. Admin only.</summary>
internal static class FinanceEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/expenses", ListExpensesAsync).RequireTenantRole(TenantRole.Admin).WithName("ListExpenses");
        group.MapPost("/expenses", CreateExpenseAsync).RequireTenantRole(TenantRole.Admin).WithName("CreateExpense");
        group.MapPut("/expenses/{id:guid}", UpdateExpenseAsync).RequireTenantRole(TenantRole.Admin).WithName("UpdateExpense");
        group.MapDelete("/expenses/{id:guid}", DeleteExpenseAsync).RequireTenantRole(TenantRole.Admin).WithName("DeleteExpense");
        group.MapGet("/report", ReportAsync).RequireTenantRole(TenantRole.Admin).WithName("GetFinanceReport");
        group.MapGet("/report/export", ExportAsync).RequireTenantRole(TenantRole.Admin).WithName("ExportFinanceReport");
    }

    /// <summary>Every expense: fixed ones first, newest first.</summary>
    private static async Task<Ok<List<ExpenseDto>>> ListExpensesAsync(AppDbContext db, CancellationToken ct)
    {
        var expenses = await db.Set<Expense>().AsNoTracking()
            .OrderBy(e => e.Kind).ThenByDescending(e => e.Date).ThenBy(e => e.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(expenses.Select(FinanceCalculator.ToDto).ToList());
    }

    private static async Task<Results<Created<ExpenseDto>, ValidationProblem>> CreateExpenseAsync(
        SaveExpenseRequest request, AppDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(request, db, ct) is { IsValid: false } errors)
        {
            return errors.ToProblem();
        }

        var expense = Expense.Create(request.Kind, request.Name, request.Amount, request.LocationId, request.Date, request.EndsOn);
        db.Add(expense);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/finance/expenses/{expense.Id}", FinanceCalculator.ToDto(expense));
    }

    private static async Task<Results<Ok<ExpenseDto>, NotFound, ValidationProblem>> UpdateExpenseAsync(
        Guid id, SaveExpenseRequest request, AppDbContext db, CancellationToken ct)
    {
        var expense = await db.Set<Expense>().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (expense is null)
        {
            return TypedResults.NotFound();
        }

        if (await ValidateAsync(request, db, ct) is { IsValid: false } errors)
        {
            return errors.ToProblem();
        }

        expense.Update(request.Kind, request.Name, request.Amount, request.LocationId, request.Date, request.EndsOn);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FinanceCalculator.ToDto(expense));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteExpenseAsync(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.Set<Expense>().Where(e => e.Id == id).ExecuteDeleteAsync(ct) == 0 ? TypedResults.NotFound() : TypedResults.NoContent();

    /// <param name="month">yyyy-MM; the current month of the business when omitted.</param>
    /// <param name="locationId">Only this location's appointments and expenses (plus the business-wide expenses).</param>
    private static async Task<Results<Ok<FinanceReportDto>, ValidationProblem>> ReportAsync(
        string? month, Guid? locationId, AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, TimeProvider clock,
        CancellationToken ct)
    {
        var (report, error) = await BuildAsync(month, locationId, db, tenant, tenants, clock, ct);
        return error is not null ? error : TypedResults.Ok(report!);
    }

    /// <param name="format">"pdf" or "xlsx".</param>
    private static async Task<Results<FileContentHttpResult, ValidationProblem>> ExportAsync(
        string? month, Guid? locationId, string format, AppDbContext db, ITenantContext tenant, ITenantDirectory tenants,
        TimeProvider clock, CancellationToken ct)
    {
        if (format is not ("pdf" or "xlsx"))
        {
            return new ValidationErrors().Add("format", "format.invalid").ToProblem();
        }

        var (report, error) = await BuildAsync(month, locationId, db, tenant, tenants, clock, ct);
        if (error is not null)
        {
            return error;
        }

        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], ct))[tenant.TenantId!.Value];
        var locations = await db.Set<Location>().AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var document = new FinanceDocument(report!, business.Name, business.DefaultLanguage,
            locationId is { } id ? locations.GetValueOrDefault(id) : null, locations);
        var fileName = $"{business.Slug}-{report!.Month}";
        return format == "pdf"
            ? TypedResults.File(FinanceExport.Pdf(document), "application/pdf", fileName + ".pdf")
            : TypedResults.File(FinanceExport.Excel(document), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName + ".xlsx");
    }

    private static async Task<(FinanceReportDto? Report, ValidationProblem? Error)> BuildAsync(
        string? month, Guid? locationId, AppDbContext db, ITenantContext tenant, ITenantDirectory tenants, TimeProvider clock,
        CancellationToken ct)
    {
        var business = (await tenants.GetByIdsAsync([tenant.TenantId!.Value], ct))[tenant.TenantId!.Value];
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var now = clock.GetUtcNow();
        DateOnly first;
        if (string.IsNullOrWhiteSpace(month))
        {
            first = Expense.FirstOfMonth(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime));
        }
        else if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            first = parsed;
        }
        else
        {
            return (null, new ValidationErrors().Add("month", "month.invalid").ToProblem());
        }

        var appointments = await LoadAsync(db, first, locationId, timeZone, ct);
        var previous = await LoadAsync(db, first.AddMonths(-1), locationId, timeZone, ct);
        var expenses = await db.Set<Expense>().AsNoTracking()
            .Where(e => locationId == null || e.LocationId == null || e.LocationId == locationId)
            .ToListAsync(ct);
        var staffNames = await db.Set<StaffMember>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        return (FinanceCalculator.Build(first, locationId, timeZone, now, appointments, previous, expenses, staffNames), null);
    }

    /// <summary>Non-cancelled appointments starting in the business-local month.</summary>
    private static Task<List<FinanceAppointment>> LoadAsync(
        AppDbContext db, DateOnly month, Guid? locationId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var from = Instant(month, timeZone);
        var to = Instant(month.AddMonths(1), timeZone);
        return db.Set<Appointment>().AsNoTracking()
            .Where(a => a.Status != AppointmentStatus.Cancelled && a.StartsAt >= from && a.StartsAt < to
                        && (locationId == null || a.LocationId == locationId))
            .Select(a => new FinanceAppointment(a.StaffMemberId, a.ServiceName, a.Price, a.ChargedAmount, a.Status, a.StartsAt, a.EndsAt))
            .ToListAsync(ct);
    }

    private static DateTimeOffset Instant(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static async Task<ValidationErrors> ValidateAsync(SaveExpenseRequest r, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors()
            .AddIf(!Enum.IsDefined(r.Kind), "kind", "expense.kind")
            .AddIf(string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > Expense.NameMaxLength, "name", "expense.name")
            .AddIf(r.Amount <= 0 || r.Amount > Expense.MaxAmount, "amount", "expense.amount")
            .AddIf(r.Kind == ExpenseKind.Fixed && r.EndsOn is { } end && Expense.FirstOfMonth(end) < Expense.FirstOfMonth(r.Date),
                "endsOn", "expense.endsBeforeStart");
        if (r.LocationId is { } locationId && !await db.Set<Location>().AnyAsync(l => l.Id == locationId, ct))
        {
            errors.Add("locationId", "location.notFound");
        }

        return errors;
    }
}
