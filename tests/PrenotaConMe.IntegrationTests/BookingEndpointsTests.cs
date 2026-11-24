using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-08 availability and F1-09 booking, including the database no-overlap guarantee.</summary>
public sealed class BookingEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>A Wednesday at least a week ahead: inside the default opening hours (Tue–Sat 09–19), never in the past.</summary>
    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, ServiceDto Combo);

    private async Task<Setup> CreateBusinessAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var services = await business.GetAsync<List<ServiceDto>>("/api/catalog/services");
        return new Setup(business, location.Id, services.Single(s => s.Name == "Taglio capelli"), services.Single(s => s.Name == "Taglio + barba"));
    }

    private static async Task<Guid> AddStaffAsync(Setup s, string name, int fromHour, int toHour)
    {
        var created = await s.Business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest(name, $"{name}-{Guid.NewGuid():N}@example.com", true, [s.LocationId], [s.Haircut.Id, s.Combo.Id]),
            TestJson.Options);
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!;

        var hours = await s.Business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}/working-hours",
            new[] { new WorkingHoursDto(s.LocationId, Day.DayOfWeek, new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0)) },
            TestJson.Options);
        hours.EnsureSuccessStatusCode();
        return staff.Id;
    }

    private HttpClient Anonymous(Setup s)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", s.Business.Slug);
        return client;
    }

    private HttpClient Client(Setup s, string name) => factory.CreateUserClient($"{name}-{s.Business.Slug}", s.Business.Slug);

    private static Task<AvailabilityDto?> AvailabilityAsync(HttpClient client, Setup s, Guid serviceId, Guid? staffId = null) =>
        client.GetFromJsonAsync<AvailabilityDto>(
            $"/api/scheduling/availability?locationId={s.LocationId}&serviceId={serviceId}&from={Day:yyyy-MM-dd}&days=1"
            + (staffId is null ? string.Empty : $"&staffId={staffId}"),
            TestJson.Options);

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Setup s, Guid serviceId, Guid? staffId, int hour, int minute = 0) =>
        client.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, serviceId, staffId, RomeTime(hour, minute)), TestJson.Options);

    private static DateTimeOffset RomeTime(int hour, int minute = 0)
    {
        var local = Day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    [Fact]
    public async Task Availability_is_public_and_follows_working_hours()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);

        var availability = await AvailabilityAsync(Anonymous(s), s, s.Combo.Id);

        var day = Assert.Single(availability!.Days);
        Assert.Equal(Day, day.Date);
        Assert.Equal(["09:00", "09:30", "10:00", "10:30", "11:00"], day.Slots.Select(x => x.Time).ToArray());
        Assert.All(day.Slots, slot => Assert.Equal([mario], slot.StaffIds));
        Assert.Equal("Europe/Rome", availability.TimeZone);
    }

    [Fact]
    public async Task Availability_requires_the_business_header()
    {
        var s = await CreateBusinessAsync();

        var response = await factory.CreateClient().GetAsync(new Uri(
            $"/api/scheduling/availability?locationId={s.LocationId}&serviceId={s.Haircut.Id}&from={Day:yyyy-MM-dd}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booking_requires_sign_in()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);

        var response = await BookAsync(Anonymous(s), s, s.Haircut.Id, mario, 9);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Booking_takes_the_slot_and_makes_the_user_a_client()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);
        var client = Client(s, "anna");

        var booked = await BookAsync(client, s, s.Combo.Id, mario, 10);
        var appointment = await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        Assert.Equal(RomeTime(10), appointment!.StartsAt);
        Assert.Equal(RomeTime(11), appointment.EndsAt);
        Assert.Equal(s.Combo.Price, appointment.Price);

        // 10:00–11:00 is taken: 09:30 (would end 10:30) and 10:00/10:30 are gone; 09:00 (ends 10:00) and 11:00 remain.
        var times = (await AvailabilityAsync(Anonymous(s), s, s.Combo.Id))!.Days.Single().Slots.Select(x => x.Time).ToArray();
        Assert.Equal(["09:00", "11:00"], times);

        var me = await client.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);
        Assert.Contains(me!.Memberships, m => m.TenantSlug == s.Business.Slug && m.Role == TenantRole.Client);
    }

    [Fact]
    public async Task Booking_a_taken_or_invalid_time_is_refused()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);
        await BookAsync(Client(s, "first"), s, s.Haircut.Id, mario, 9);

        var taken = await BookAsync(Client(s, "second"), s, s.Haircut.Id, mario, 9);
        var offGrid = await BookAsync(Client(s, "third"), s, s.Haircut.Id, mario, 9, 15);
        var outsideHours = await BookAsync(Client(s, "fourth"), s, s.Haircut.Id, mario, 15);

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, offGrid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, outsideHours.StatusCode);
        Assert.Contains("slot.unavailable", await taken.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_bookings_of_the_same_slot_produce_exactly_one_appointment()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => BookAsync(Client(s, $"race{i}"), s, s.Combo.Id, mario, 10)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Database_rejects_overlapping_appointments_for_the_same_staff_member()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);
        var booked = await (await BookAsync(Client(s, "anna"), s, s.Combo.Id, mario, 10)).Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options);

        // Bypass the API: insert an overlapping appointment directly.
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(
            (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(s.Business.Slug))!.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(Appointment.Book(s.LocationId, s.Combo.Id, "x", 1m, mario, Guid.CreateVersion7(), RomeTime(10, 30), 30));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(DbErrors.IsExclusionViolation(ex));
        Assert.NotNull(booked);
    }

    [Fact]
    public async Task Any_staff_is_assigned_to_the_free_person_with_fewest_appointments_that_day()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 13);
        var luigi = await AddStaffAsync(s, "luigi", 9, 13);

        // Luigi already has two appointments that day, Mario one: "any staff" must pick Mario.
        await BookAsync(Client(s, "a"), s, s.Haircut.Id, luigi, 9);
        await BookAsync(Client(s, "b"), s, s.Haircut.Id, luigi, 9, 30);
        await BookAsync(Client(s, "c"), s, s.Haircut.Id, mario, 9);

        var any = await (await BookAsync(Client(s, "d"), s, s.Haircut.Id, null, 11)).Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options);

        Assert.Equal(mario, any!.StaffMemberId);
    }

    [Fact]
    public async Task Service_with_appointments_cannot_be_deleted()
    {
        var s = await CreateBusinessAsync();
        var mario = await AddStaffAsync(s, "mario", 9, 12);
        await BookAsync(Client(s, "anna"), s, s.Haircut.Id, mario, 9);

        var service = await s.Business.Owner.DeleteAsync(new Uri($"/api/catalog/services/{s.Haircut.Id}", UriKind.Relative));
        var staff = await s.Business.Owner.DeleteAsync(new Uri($"/api/staff/{mario}", UriKind.Relative));
        var location = await s.Business.Owner.DeleteAsync(new Uri($"/api/locations/{s.LocationId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, service.StatusCode);
        Assert.Contains("service.inUse", await service.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, staff.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, location.StatusCode);
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
