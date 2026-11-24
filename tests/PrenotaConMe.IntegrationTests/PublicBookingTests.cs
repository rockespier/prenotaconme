using System.Net;
using System.Net.Http.Json;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Users;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F1-10: data for the public booking page and the client's phone number.</summary>
public sealed class PublicBookingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Booking_options_are_public_and_only_list_bookable_things()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var services = await business.GetAsync<List<ServiceDto>>("/api/catalog/services");

        await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", "mario@example.com", true, [location.Id], [services[0].Id]), TestJson.Options);
        await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Retired", "old@example.com", false, [location.Id], []), TestJson.Options);
        var hidden = services[1];
        await business.Owner.PutAsJsonAsync($"/api/catalog/services/{hidden.Id}",
            new SaveServiceRequest(hidden.Name, null, hidden.DurationMinutes, hidden.Price, false, hidden.LocationIds), TestJson.Options);

        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", business.Slug);
        var response = await anonymous.GetAsync(new Uri("/api/scheduling/booking-options", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        var options = await response.Content.ReadFromJsonAsync<BookingOptionsDto>(TestJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(location.Id, Assert.Single(options!.Locations).Id);
        Assert.Equal(2, options.Services.Count);
        Assert.DoesNotContain(options.Services, s => s.Id == hidden.Id);
        Assert.Equal("Mario", Assert.Single(options.Staff).DisplayName);
        Assert.DoesNotContain("mario@example.com", body, StringComparison.Ordinal); // no personal data
    }

    [Fact]
    public async Task Client_sets_phone_number_in_international_format()
    {
        var client = factory.CreateUserClient("phone-" + Guid.NewGuid().ToString("N")[..8]);

        var invalid = await client.PutAsJsonAsync("/api/users/me/phone", new SetPhoneRequest("333 1234567"), TestJson.Options);
        var valid = await client.PutAsJsonAsync("/api/users/me/phone", new SetPhoneRequest("+39 333 123 4567"), TestJson.Options);
        var me = await client.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("phone.invalid", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        Assert.Equal("+393331234567", me!.PhoneNumber);
    }
}
