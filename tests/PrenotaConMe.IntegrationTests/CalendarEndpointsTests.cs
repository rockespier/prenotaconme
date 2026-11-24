using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-13: backoffice calendar (Admin manages everything, Staff see only their own appointments).</summary>
public sealed class CalendarEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private static readonly DateOnly Day = NextWednesday();

    private sealed record Setup(TestBusiness Business, Guid LocationId, ServiceDto Haircut, Guid Mario, Guid Luigi, HttpClient MarioClient);

    private async Task<Setup> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var marioUid = "mario-" + business.Slug;

        async Task<Guid> AddStaff(string name, string email)
        {
            var created = await business.Owner.PostAsJsonAsync("/api/staff",
                new SaveStaffRequest(name, email, true, [location.Id], [haircut.Id]), TestJson.Options);
            var id = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
            (await business.Owner.PutAsJsonAsync($"/api/staff/{id}/working-hours",
                new[] { new WorkingHoursDto(location.Id, Day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
                .EnsureSuccessStatusCode();
            return id;
        }

        var mario = await AddStaff("Mario", $"{marioUid}@test.local");
        var luigi = await AddStaff("Luigi", $"luigi-{Guid.NewGuid():N}@example.com");

        // Signing in links Mario's invitation and grants the Staff role.
        var marioClient = factory.CreateUserClient(marioUid, business.Slug);
        (await marioClient.GetAsync(new Uri("/api/users/me", UriKind.Relative))).EnsureSuccessStatusCode();

        return new Setup(business, location.Id, haircut, mario, luigi, marioClient);
    }

    private static DateTimeOffset RomeTime(int hour, int minute = 0)
    {
        var local = Day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    private static string Range => $"from={Uri.EscapeDataString(RomeTime(0).ToString("O"))}&to={Uri.EscapeDataString(RomeTime(23).ToString("O"))}";

    private static Task<HttpResponseMessage> CreateGuestAsync(Setup s, Guid staffId, int hour, string name = "Giulia") =>
        s.Business.Owner.PostAsJsonAsync("/api/scheduling/calendar/appointments",
            new CreateCalendarAppointmentRequest(s.LocationId, s.Haircut.Id, staffId, RomeTime(hour), name, "+39 333 1234567"), TestJson.Options);

    private static async Task<CalendarAppointmentDto> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CalendarAppointmentDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Admin_creates_a_guest_appointment_and_sees_everyone()
    {
        var s = await CreateAsync();
        var guest = await CreatedAsync(await CreateGuestAsync(s, s.Luigi, 10));
        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);
        (await anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(s.LocationId, s.Haircut.Id, s.Mario, RomeTime(9)), TestJson.Options)).EnsureSuccessStatusCode();

        var calendar = await s.Business.GetAsync<CalendarDto>($"/api/scheduling/calendar?{Range}");

        Assert.True(calendar.CanEdit);
        Assert.Equal(2, calendar.Staff.Count);
        Assert.Equal(2, calendar.Appointments.Count);
        var booked = calendar.Appointments.Single(a => a.StaffMemberId == s.Mario);
        Assert.Equal($"anna-{s.Business.Slug}@test.local", booked.ClientEmail);
        Assert.False(booked.IsGuest);
        var walkIn = calendar.Appointments.Single(a => a.Id == guest.Id);
        Assert.True(walkIn.IsGuest);
        Assert.Equal("Giulia", walkIn.ClientName);
        Assert.Equal(RomeTime(10).AddMinutes(s.Haircut.DurationMinutes), walkIn.EndsAt);
    }

    [Fact]
    public async Task Staff_see_only_their_own_appointments_and_cannot_edit()
    {
        var s = await CreateAsync();
        await CreatedAsync(await CreateGuestAsync(s, s.Mario, 9, "Per Mario"));
        var forLuigi = await CreatedAsync(await CreateGuestAsync(s, s.Luigi, 9, "Per Luigi"));

        var calendar = await s.MarioClient.GetFromJsonAsync<CalendarDto>($"/api/scheduling/calendar?{Range}", TestJson.Options);
        var move = await s.MarioClient.PutAsJsonAsync($"/api/scheduling/calendar/appointments/{forLuigi.Id}",
            new MoveCalendarAppointmentRequest(s.Mario, RomeTime(11)), TestJson.Options);

        Assert.False(calendar!.CanEdit);
        Assert.Equal([s.Mario], calendar.Staff.Select(x => x.Id).ToArray());
        Assert.Equal("Per Mario", Assert.Single(calendar.Appointments).ClientName);
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
    }

    [Fact]
    public async Task Clients_cannot_read_the_calendar()
    {
        var s = await CreateAsync();
        var anna = factory.CreateUserClient($"anna-{s.Business.Slug}", s.Business.Slug);

        var response = await anna.GetAsync(new Uri($"/api/scheduling/calendar?{Range}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_moves_to_another_person_and_overlaps_are_refused()
    {
        var s = await CreateAsync();
        var first = await CreatedAsync(await CreateGuestAsync(s, s.Mario, 9));
        await CreatedAsync(await CreateGuestAsync(s, s.Luigi, 11));

        var moved = await s.Business.Owner.PutAsJsonAsync($"/api/scheduling/calendar/appointments/{first.Id}",
            new MoveCalendarAppointmentRequest(s.Luigi, RomeTime(10)), TestJson.Options);
        var overlap = await s.Business.Owner.PutAsJsonAsync($"/api/scheduling/calendar/appointments/{first.Id}",
            new MoveCalendarAppointmentRequest(s.Luigi, RomeTime(11)), TestJson.Options);
        var doubleBooked = await CreateGuestAsync(s, s.Luigi, 11);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var body = (await moved.Content.ReadFromJsonAsync<CalendarAppointmentDto>(TestJson.Options))!;
        Assert.Equal(s.Luigi, body.StaffMemberId);
        Assert.Equal(RomeTime(10), body.StartsAt);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, doubleBooked.StatusCode);
    }

    [Fact]
    public async Task Admin_cancels_without_notice_and_the_appointment_leaves_the_calendar()
    {
        var s = await CreateAsync();
        var appointment = await CreatedAsync(await CreateGuestAsync(s, s.Mario, 9));

        var cancelled = await s.Business.Owner.PostAsync(new Uri($"/api/scheduling/calendar/appointments/{appointment.Id}/cancel", UriKind.Relative), null);
        var calendar = await s.Business.GetAsync<CalendarDto>($"/api/scheduling/calendar?{Range}");
        var withCancelled = await s.Business.GetAsync<CalendarDto>($"/api/scheduling/calendar?{Range}&includeCancelled=true");

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Empty(calendar.Appointments);
        Assert.Equal(AppointmentStatus.Cancelled, Assert.Single(withCancelled.Appointments).Status);
    }

    [Fact]
    public async Task Create_validates_client_and_staff()
    {
        var s = await CreateAsync();

        var noName = await CreateGuestAsync(s, s.Mario, 9, " ");
        var unknownStaff = await CreateGuestAsync(s, Guid.NewGuid(), 9);

        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownStaff.StatusCode);
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
