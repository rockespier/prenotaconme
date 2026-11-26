using Microsoft.AspNetCore.SignalR;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Scheduling;

/// <summary>
/// F2-10: backoffice calendars stay connected here and refetch their range when they get
/// <see cref="ChangedMessage"/>. The message carries no data, so Staff only ever see what the calendar endpoint lets
/// them see. Only Admin/Staff of the business connect (checked on the negotiate and connect requests).
/// </summary>
public sealed class CalendarHub(ITenantDirectory tenants) : Hub
{
    public const string Path = "/calendar/hub";
    public const string ChangedMessage = "calendarChanged";

    internal static string Group(Guid tenantId) => $"calendar:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        // The hub's own scope has no tenant, so it is read again from the connect request (already authorized for it).
        var request = Context.GetHttpContext()?.Request ?? throw new HubException("Tenant required.");
        var slug = request.Headers[TenantEndpointExtensions.TenantHeader].ToString();
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = request.Query[TenantEndpointExtensions.TenantQuery].ToString();
        }

        var tenant = await tenants.FindBySlugAsync(slug, Context.ConnectionAborted) ?? throw new HubException("Tenant required.");
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenant.Id));
        await base.OnConnectedAsync();
    }
}

/// <summary>Tells the open calendars of the current business that an appointment changed.</summary>
public sealed class CalendarBroadcast(IHubContext<CalendarHub> hub, ITenantContext tenant)
{
    public Task ChangedAsync(CancellationToken ct) =>
        tenant.TenantId is { } id
            ? hub.Clients.Group(CalendarHub.Group(id)).SendAsync(CalendarHub.ChangedMessage, ct)
            : Task.CompletedTask;
}
