using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F2-10: open backoffice calendars hear about appointment changes of their own business only.</summary>
public sealed class CalendarHubTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HubConnection Connect(string uid, string slug, bool tenantInQuery = false)
    {
        var url = new Uri(factory.Server.BaseAddress, "/api/scheduling" + CalendarHub.Path + (tenantInQuery ? $"?tenant={slug}" : ""));
        return new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                // TestServer has no WebSockets listener; long polling goes through its handler.
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Headers[TestAuthHandler.Header] = $"{uid}|{uid}@test.local|{uid}";
                if (!tenantInQuery)
                {
                    o.Headers["X-Tenant"] = slug;
                }
            })
            .Build();
    }

    private static TaskCompletionSource Listen(HubConnection connection)
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On(CalendarHub.ChangedMessage, () => received.TrySetResult());
        return received;
    }

    private static async Task CreateGuestAsync(TestBusiness business)
    {
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var service = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services"))[0];
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [service.Id]), TestJson.Options);
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!;
        var start = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(9);
        (await business.Owner.PostAsJsonAsync("/api/scheduling/calendar/appointments",
            new CreateCalendarAppointmentRequest(location.Id, service.Id, staff.Id, start, "Giulia", "+39 333 1234567"), TestJson.Options))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_hears_changes_of_their_business_only()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var other = await TestBusiness.CreateAsync(factory);
        await using var mine = Connect(business.OwnerUid, business.Slug, tenantInQuery: true);
        await using var theirs = Connect(other.OwnerUid, other.Slug);
        var heard = Listen(mine);
        var otherHeard = Listen(theirs);
        await mine.StartAsync();
        await theirs.StartAsync();

        await CreateGuestAsync(business);

        await heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        Assert.False(otherHeard.Task.IsCompleted);
    }

    [Fact]
    public async Task Someone_without_a_backoffice_role_cannot_connect()
    {
        var business = await TestBusiness.CreateAsync(factory);
        await using var stranger = Connect("stranger-" + business.Slug, business.Slug);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => stranger.StartAsync());
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);
    }
}
