using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Auth;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-04..F1-07: locations, services, staff, working hours, time off.</summary>
public sealed class SetupEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeOnly T9 = new(9, 0);
    private static readonly TimeOnly T13 = new(13, 0);
    private static readonly TimeOnly T19 = new(19, 0);

    private static SaveLocationRequest NewLocation(string name = "Centro") =>
        new(name, "Via Roma 1", "Milano", null, "Europe/Rome", true,
            [new OpeningHoursDto(DayOfWeek.Monday, T9, T19), new OpeningHoursDto(DayOfWeek.Tuesday, T9, T19)]);

    [Fact]
    public async Task New_business_starts_with_a_location_and_sample_services()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var location = Assert.Single(await business.GetAsync<List<LocationDto>>("/api/locations"));
        var services = await business.GetAsync<List<ServiceDto>>("/api/catalog/services");

        Assert.Equal("Sede principale", location.Name);
        Assert.Equal(5, location.OpeningHours.Count);
        Assert.Equal(3, services.Count);
        Assert.All(services, s => Assert.Equal([location.Id], s.LocationIds));
        Assert.Contains(services, s => s is { Name: "Taglio + barba", DurationMinutes: 60 });
    }

    [Fact]
    public async Task Location_crud()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var created = await business.Owner.PostAsJsonAsync("/api/locations", NewLocation(), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = (await created.Content.ReadFromJsonAsync<LocationDto>(TestJson.Options))!;

        var updated = await business.Owner.PutAsJsonAsync($"/api/locations/{location.Id}", NewLocation("Centro 2"), TestJson.Options);
        Assert.Equal("Centro 2", (await updated.Content.ReadFromJsonAsync<LocationDto>(TestJson.Options))!.Name);

        var deleted = await business.Owner.DeleteAsync(new Uri($"/api/locations/{location.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Single(await business.GetAsync<List<LocationDto>>("/api/locations"));
    }

    [Fact]
    public async Task Location_validation()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var invalid = NewLocation() with
        {
            TimeZone = "Mars/Base",
            OpeningHours = [new OpeningHoursDto(DayOfWeek.Monday, T13, T9)],
        };

        var response = await business.Owner.PostAsJsonAsync("/api/locations", invalid, TestJson.Options);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeZone.invalid", body, StringComparison.Ordinal);
        Assert.Contains("openingHours.interval.order", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Businesses_cannot_see_or_use_each_others_data()
    {
        var a = await TestBusiness.CreateAsync(factory);
        var b = await TestBusiness.CreateAsync(factory);
        var locationOfA = (await a.GetAsync<List<LocationDto>>("/api/locations"))[0];

        var read = await b.Owner.GetAsync(new Uri($"/api/locations/{locationOfA.Id}", UriKind.Relative));
        var useInService = await b.Owner.PostAsJsonAsync("/api/catalog/services",
            new SaveServiceRequest("Taglio", null, 30, 10m, true, [locationOfA.Id]), TestJson.Options);
        var delete = await b.Owner.DeleteAsync(new Uri($"/api/locations/{locationOfA.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, useInService.StatusCode);
        Assert.Contains("locations.unknown", await useInService.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Single(await a.GetAsync<List<LocationDto>>("/api/locations"));
    }

    [Fact]
    public async Task Service_crud_and_validation()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var locationId = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0].Id;

        var invalid = await business.Owner.PostAsJsonAsync("/api/catalog/services",
            new SaveServiceRequest("Colore", null, 45, -1m, true, []), TestJson.Options);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("duration.invalid", invalidBody, StringComparison.Ordinal);
        Assert.Contains("price.invalid", invalidBody, StringComparison.Ordinal);
        Assert.Contains("locations.required", invalidBody, StringComparison.Ordinal);

        var created = await business.Owner.PostAsJsonAsync("/api/catalog/services",
            new SaveServiceRequest("Colore", "Tinta completa", 90, 45.5m, true, [locationId]), TestJson.Options);
        var service = (await created.Content.ReadFromJsonAsync<ServiceDto>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(45.5m, service.Price);

        // Adding a location to an existing service inserts a new link row (regression: client-generated ids).
        var second = (await (await business.Owner.PostAsJsonAsync("/api/locations", NewLocation("Second"), TestJson.Options))
            .Content.ReadFromJsonAsync<LocationDto>(TestJson.Options))!;
        var updated = await business.Owner.PutAsJsonAsync($"/api/catalog/services/{service.Id}",
            new SaveServiceRequest("Colore", null, 90, 45.5m, true, [locationId, second.Id]), TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await business.GetAsync<ServiceDto>($"/api/catalog/services/{service.Id}")).LocationIds.Count);

        var deleted = await business.Owner.DeleteAsync(new Uri($"/api/catalog/services/{service.Id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Staff_schedule_working_hours_and_time_off()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0]; // Tue–Sat 09–19
        var serviceIds = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Select(s => s.Id).ToList();

        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", "Mario@Example.com", true, [location.Id], serviceIds), TestJson.Options);
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("mario@example.com", staff.Email);
        Assert.False(staff.IsLinked);

        var outside = await business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}/working-hours",
            new[] { new WorkingHoursDto(location.Id, DayOfWeek.Monday, T9, T13) }, TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains("workingHours.outsideOpeningHours", await outside.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var ok = await business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}/working-hours",
            new[] { new WorkingHoursDto(location.Id, DayOfWeek.Tuesday, T9, T13), new WorkingHoursDto(location.Id, DayOfWeek.Tuesday, new(14, 0), T19) },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var timeOff = await business.Owner.PostAsJsonAsync($"/api/staff/{staff.Id}/time-off",
            new AddTimeOffRequest(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), "Natale"), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, timeOff.StatusCode);

        var schedule = await business.GetAsync<StaffScheduleDto>($"/api/staff/{staff.Id}/schedule");
        Assert.Equal(2, schedule.WorkingHours.Count);
        Assert.Equal("Natale", Assert.Single(schedule.TimeOff).Reason);

        // Removing the location from the person drops their hours there.
        await business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}",
            new SaveStaffRequest("Mario", "mario@example.com", true, [], serviceIds), TestJson.Options);
        Assert.Empty((await business.GetAsync<StaffScheduleDto>($"/api/staff/{staff.Id}/schedule")).WorkingHours);
    }

    [Fact]
    public async Task Duplicate_staff_email_is_a_conflict()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var request = new SaveStaffRequest("Luigi", "luigi@example.com", true, [], []);

        await business.Owner.PostAsJsonAsync("/api/staff", request, TestJson.Options);
        var second = await business.Owner.PostAsJsonAsync("/api/staff", request with { DisplayName = "Luigi 2" }, TestJson.Options);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invited_staff_is_linked_on_sign_in_and_gets_read_only_access()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var barberUid = "barber-" + business.Slug;
        var barberEmail = $"{barberUid}@test.local"; // ApiFactory derives the e-mail from the uid

        await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Barber", barberEmail.ToUpperInvariant(), true, [], []), TestJson.Options);

        var barber = factory.CreateUserClient(barberUid, business.Slug);
        var forbiddenBefore = await barber.GetAsync(new Uri("/api/staff", UriKind.Relative));
        var me = await barber.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenBefore.StatusCode);
        Assert.Contains(me!.Memberships, m => m.TenantSlug == business.Slug && m.Role == TenantRole.Staff);
        Assert.Equal(HttpStatusCode.OK, (await barber.GetAsync(new Uri("/api/staff", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await barber.PostAsJsonAsync("/api/locations", NewLocation(), TestJson.Options)).StatusCode);

        var staff = Assert.Single(await business.GetAsync<List<StaffDto>>("/api/staff"));
        Assert.True(staff.IsLinked);
    }

    [Fact]
    public async Task Admin_adding_themselves_is_linked_immediately()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Owner", $"{business.OwnerUid}@test.local", true, [], []), TestJson.Options);
        var me = await business.Owner.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);

        Assert.True((await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.IsLinked);
        Assert.Contains(me!.Memberships, m => m.Role == TenantRole.Staff);
        Assert.Contains(me.Memberships, m => m.Role == TenantRole.Admin);
    }
}
