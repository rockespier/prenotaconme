using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-14: client list and history, built from the business's appointments.</summary>
public sealed class ClientsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, Guid Mario);

    private async Task<Setup> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, Day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(18, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();
        return new Setup(business, location.Id, haircut, mario);
    }

    private static DateTimeOffset RomeTime(DateOnly day, int hour)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    private static async Task<CalendarAppointmentDto> GuestAsync(Setup s, DateTimeOffset at, string name, string? phone)
    {
        var response = await s.Business.Owner.PostAsJsonAsync("/api/scheduling/calendar/appointments",
            new CreateCalendarAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, at, name, phone), TestJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CalendarAppointmentDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Lists_registered_and_guest_clients_with_stats()
    {
        var s = await CreateAsync();
        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);
        (await anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(Day, 9)), TestJson.Options)).EnsureSuccessStatusCode();

        // Same phone = same guest; one visit in the past (Admin may record it), one upcoming, one cancelled.
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await GuestAsync(s, RomeTime(yesterday, 10), "Giulia", "+39 333 111");
        await GuestAsync(s, RomeTime(Day, 11), "Giulia R.", "+39 333 111");
        var cancelled = await GuestAsync(s, RomeTime(Day, 14), "Giulia R.", "+39 333 111");
        await s.Business.Owner.PostAsync(new Uri($"/api/scheduling/calendar/appointments/{cancelled.Id}/cancel", UriKind.Relative), null);
        await GuestAsync(s, RomeTime(Day, 15), "Walk-in senza telefono", null);

        var clients = await s.Business.GetAsync<List<ClientSummaryDto>>("/api/scheduling/clients");

        Assert.Equal(3, clients.Count);
        var registered = clients.Single(c => !c.IsGuest);
        Assert.Equal($"anna-{s.Business.Slug}@test.local", registered.Email);
        Assert.Equal(RomeTime(Day, 9), registered.NextAppointment);

        var giulia = clients.Single(c => c.Phone == "+39 333 111");
        Assert.True(giulia.IsGuest);
        Assert.Equal("Giulia R.", giulia.Name);
        Assert.Equal(1, giulia.Visits);
        Assert.Equal(1, giulia.Cancelled);
        Assert.Equal(s.Haircut.Price, giulia.Spent);
        Assert.Equal(RomeTime(yesterday, 10), giulia.LastVisit);
        Assert.Equal(RomeTime(Day, 11), giulia.NextAppointment);
        Assert.Equal(cancelled.ClientKey, giulia.Key);

        Assert.Single(clients, c => c.Name == "Walk-in senza telefono");
    }

    [Fact]
    public async Task Search_matches_name_email_or_phone()
    {
        var s = await CreateAsync();
        await GuestAsync(s, RomeTime(Day, 10), "Giulia", "+39 333 111");
        await GuestAsync(s, RomeTime(Day, 11), "Paolo", "+39 333 222");

        var byName = await s.Business.GetAsync<List<ClientSummaryDto>>("/api/scheduling/clients?search=giu");
        var byPhone = await s.Business.GetAsync<List<ClientSummaryDto>>("/api/scheduling/clients?search=222");

        Assert.Equal("Giulia", Assert.Single(byName).Name);
        Assert.Equal("Paolo", Assert.Single(byPhone).Name);
    }

    [Fact]
    public async Task Detail_returns_the_history_of_one_client()
    {
        var s = await CreateAsync();
        var first = await GuestAsync(s, RomeTime(Day, 10), "Giulia", "+39 333 111");
        await GuestAsync(s, RomeTime(Day, 12), "Giulia", "+39 333 111");
        await GuestAsync(s, RomeTime(Day, 14), "Paolo", "+39 333 222");

        var detail = await s.Business.GetAsync<ClientDetailDto>($"/api/scheduling/clients/detail?key={Uri.EscapeDataString(first.ClientKey)}");
        var missing = await s.Business.Owner.GetAsync(new Uri("/api/scheduling/clients/detail?key=guest:nobody", UriKind.Relative));

        Assert.Equal("Giulia", detail.Client.Name);
        Assert.Equal(2, detail.Appointments.Count);
        Assert.Equal(RomeTime(Day, 12), detail.Appointments[0].StartsAt);
        Assert.All(detail.Appointments, a => Assert.Equal("Mario", a.StaffName));
        Assert.All(detail.Appointments, a => Assert.Equal(AppointmentStatus.Confirmed, a.Status));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Clients_are_admin_only()
    {
        var s = await CreateAsync();
        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);

        var response = await anna.GetAsync(new Uri("/api/scheduling/clients", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static DateOnly NextWednesday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (date.DayOfWeek != DayOfWeek.Wednesday)
        {
            date = date.AddDays(1);
        }

        return date;
    }
}
