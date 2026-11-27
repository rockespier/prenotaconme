using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Chatbot;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Scheduling.Domain;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F5-04: download my data, delete my account, a business erasing a client, chat retention.</summary>
public sealed class GdprTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private sealed record Setup(TestBusiness Business, HttpClient Client, string ClientUid, Guid AppointmentId);

    /// <summary>A business with one staff member working next Wednesday 9–12, and a client who booked at 10.</summary>
    private async Task<Setup> BookAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Wednesday)
        {
            day = day.AddDays(1);
        }

        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var staff = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!;
        (await business.Owner.PutAsJsonAsync($"/api/staff/{staff.Id}/working-hours",
            new[] { new WorkingHoursDto(location.Id, day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        var clientUid = "anna-" + business.Slug;
        var client = factory.CreateUserClient(clientUid, business.Slug);
        var local = day.ToDateTime(new TimeOnly(10, 0));
        var booked = await client.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(location.Id, haircut.Id, staff.Id, new DateTimeOffset(local, Rome.GetUtcOffset(local))), TestJson.Options);
        booked.EnsureSuccessStatusCode();
        var appointment = (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;
        return new Setup(business, client, clientUid, appointment.Id);
    }

    private async Task<Appointment> ReadAppointmentAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Appointment>()
            .IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task A_client_downloads_their_data()
    {
        var s = await BookAsync();

        var response = await s.Client.GetAsync(new Uri("/api/users/me/export", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"{s.ClientUid}@test.local", json.RootElement.GetProperty("account").GetProperty("email").GetString());
        var appointment = json.RootElement.GetProperty("appointments")[0];
        Assert.Equal("Test business", appointment.GetProperty("business").GetString());
        Assert.Equal("Taglio capelli", appointment.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Deleting_the_account_anonymizes_and_cancels_future_appointments()
    {
        var s = await BookAsync();

        var response = await s.Client.DeleteAsync(new Uri("/api/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var appointment = await ReadAppointmentAsync(s.AppointmentId);
        Assert.Null(appointment.ClientUserId);
        Assert.Equal(Appointment.ErasedClientName, appointment.ClientName);
        Assert.Null(appointment.ClientPhone);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);

        // Signing in again starts a new, empty account.
        var me = (await s.Client.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options))!;
        Assert.Empty(me.Memberships);
    }

    [Fact]
    public async Task Admins_must_hand_over_before_deleting_their_account()
    {
        var business = await TestBusiness.CreateAsync(factory);

        var response = await business.Owner.DeleteAsync(new Uri("/api/users/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("account.hasBusinessRole", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_business_erases_a_client_on_request()
    {
        var s = await BookAsync();
        var clients = await s.Business.GetAsync<List<ClientSummaryDto>>("/api/scheduling/clients");
        var key = clients.Single(c => !c.IsGuest).Key;

        var response = await s.Business.Owner.PostAsync(
            new Uri($"/api/scheduling/clients/erase?key={Uri.EscapeDataString(key)}", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Appointment.ErasedClientName, (await ReadAppointmentAsync(s.AppointmentId)).ClientName);
        var me = (await s.Client.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options))!;
        Assert.DoesNotContain(me.Memberships, m => m.TenantSlug == s.Business.Slug);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Business.Owner.PostAsync(
            new Uri($"/api/scheduling/clients/erase?key={Uri.EscapeDataString(key)}", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Old_chats_are_deleted_by_the_retention_job()
    {
        var s = await BookAsync();
        var me = (await s.Client.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options))!;

        await using var scope = factory.Services.CreateAsyncScope();
        var tenantId = (await scope.ServiceProvider.GetRequiredService<ITenantDirectory>().FindBySlugAsync(s.Business.Slug))!.Id;
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = ChatConversation.Start("telegram", "old-" + s.Business.Slug, me.Id);
        var recent = ChatConversation.Start("telegram", "new-" + s.Business.Slug, me.Id);
        db.AddRange(old, recent);
        await db.SaveChangesAsync();
        await db.Set<ChatConversation>().Where(c => c.Id == recent.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastMessageAt, DateTimeOffset.UtcNow));

        await ActivatorUtilities.CreateInstance<ChatRetentionJob>(scope.ServiceProvider).RunAsync(CancellationToken.None);

        var left = await db.Set<ChatConversation>().AsNoTracking().Select(c => c.Id).ToListAsync();
        Assert.Equal([recent.Id], left);
    }
}
