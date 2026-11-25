using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Modules.Catalog;
using PrenotaConMe.Modules.Locations;
using PrenotaConMe.Modules.Notifications.Reminders;
using PrenotaConMe.Modules.Scheduling;
using PrenotaConMe.Modules.Staff;
using PrenotaConMe.Modules.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>
/// F2-06: reminders are delayed Hangfire jobs. The tests find them in the schedule and run them now (requeue)
/// instead of waiting for the real time.
/// </summary>
public sealed class ReminderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private sealed record Setup(TestBusiness Business, HttpClient Anna, string AnnaEmail, Guid Mario, DateOnly Day);

    private JobStorage Storage => factory.Services.GetRequiredService<JobStorage>();

    private static DateTimeOffset RomeTime(DateOnly day, int hour)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, Rome.GetUtcOffset(local));
    }

    private async Task<Setup> CreateAsync()
    {
        var business = await TestBusiness.CreateAsync(factory);
        var location = (await business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(s => s.Name == "Taglio capelli");
        var created = await business.Owner.PostAsJsonAsync("/api/staff",
            new SaveStaffRequest("Mario", $"mario-{Guid.NewGuid():N}@example.com", true, [location.Id], [haircut.Id]), TestJson.Options);
        var mario = (await created.Content.ReadFromJsonAsync<StaffDto>(TestJson.Options))!.Id;
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (day.DayOfWeek != DayOfWeek.Wednesday)
        {
            day = day.AddDays(1);
        }

        (await business.Owner.PutAsJsonAsync($"/api/staff/{mario}/working-hours",
            new[] { new WorkingHoursDto(location.Id, day.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0)) }, TestJson.Options))
            .EnsureSuccessStatusCode();

        var uid = $"anna-{business.Slug}";
        return new Setup(business, factory.CreateUserClient(uid, business.Slug), $"{uid}@test.local", mario, day);
    }

    private static async Task<AppointmentDto> BookAsync(Setup s, int hour)
    {
        var location = (await s.Business.GetAsync<List<LocationDto>>("/api/locations"))[0];
        var haircut = (await s.Business.GetAsync<List<ServiceDto>>("/api/catalog/services")).Single(x => x.Name == "Taglio capelli");
        var booked = await s.Anna.PostAsJsonAsync("/api/scheduling/appointments",
            new BookAppointmentRequest(location.Id, haircut.Id, s.Mario, RomeTime(s.Day, hour)), TestJson.Options);
        booked.EnsureSuccessStatusCode();
        return (await booked.Content.ReadFromJsonAsync<AppointmentDto>(TestJson.Options))!;
    }

    /// <summary>Scheduled reminder jobs of one appointment: job id → (hours before, run at).</summary>
    private List<(string Id, int Hours, DateTime At)> ScheduledReminders(Guid appointmentId) =>
        Storage.GetMonitoringApi().ScheduledJobs(0, 10_000)
            .Where(j => j.Value.Job?.Type == typeof(AppointmentReminderJob) && (Guid)j.Value.Job.Args[0] == appointmentId)
            .Select(j => (j.Key, (int)j.Value.Job.Args[2], j.Value.EnqueueAt))
            .OrderByDescending(j => j.Item2)
            .ToList();

    /// <summary>Runs a scheduled job now and waits until it has finished.</summary>
    private async Task RunNowAsync(string jobId)
    {
        new BackgroundJobClient(Storage).Requeue(jobId);
        for (var i = 0; i < 200; i++)
        {
            var state = Storage.GetMonitoringApi().JobDetails(jobId)?.History.FirstOrDefault()?.StateName;
            if (state is "Succeeded" or "Failed")
            {
                Assert.Equal("Succeeded", state);
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Job {jobId} did not finish");
    }

    private Task<IReadOnlyList<Modules.Notifications.EmailMessage>> RemindersTo(string email, int count = 1) =>
        factory.Emails.WaitForAsync(m => m.To == email && m.Subject.StartsWith("Promemoria:", StringComparison.Ordinal), count);

    [Fact]
    public async Task Booking_schedules_24h_and_2h_reminders_and_the_client_gets_them()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, 10);

        var jobs = ScheduledReminders(appointment.Id);
        await RunNowAsync(jobs[1].Id);
        var emails = await RemindersTo(s.AnnaEmail);

        Assert.Equal([24, 2], jobs.Select(j => j.Hours));
        Assert.Equal(appointment.StartsAt.UtcDateTime.AddHours(-24), jobs[0].At, TimeSpan.FromSeconds(1));
        Assert.Single(emails);
        Assert.Contains("ti ricordiamo il tuo appuntamento", emails[0].TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moved_or_cancelled_appointments_skip_old_reminders()
    {
        var s = await CreateAsync();
        var moved = await BookAsync(s, 9);
        var cancelled = await BookAsync(s, 11);
        var oldJob = ScheduledReminders(moved.Id)[0].Id;

        (await s.Anna.PostAsJsonAsync($"/api/scheduling/appointments/{moved.Id}/reschedule",
            new RescheduleAppointmentRequest(RomeTime(s.Day, 10), s.Mario), TestJson.Options)).EnsureSuccessStatusCode();
        (await s.Anna.PostAsync(new Uri($"/api/scheduling/appointments/{cancelled.Id}/cancel", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        var afterMove = ScheduledReminders(moved.Id);
        await RunNowAsync(oldJob);
        await RunNowAsync(ScheduledReminders(cancelled.Id)[0].Id);

        Assert.Equal(4, afterMove.Count); // the 2 old ones (skipped when they run) + 24 h and 2 h before the new time
        Assert.Empty(await RemindersTo(s.AnnaEmail));

        await RunNowAsync(afterMove.First(j => j.Id != oldJob && j.Hours == 24).Id);
        Assert.Single(await RemindersTo(s.AnnaEmail));
    }

    [Fact]
    public async Task Client_can_turn_reminders_off()
    {
        var s = await CreateAsync();
        var appointment = await BookAsync(s, 10);

        var before = await s.Anna.GetFromJsonAsync<ReminderPreferenceDto>("/api/notifications/reminders/preference", TestJson.Options);
        (await s.Anna.PutAsJsonAsync("/api/notifications/reminders/preference", new ReminderPreferenceDto(false), TestJson.Options))
            .EnsureSuccessStatusCode();
        var after = await s.Anna.GetFromJsonAsync<ReminderPreferenceDto>("/api/notifications/reminders/preference", TestJson.Options);
        await RunNowAsync(ScheduledReminders(appointment.Id)[0].Id);

        Assert.True(before!.Enabled);
        Assert.False(after!.Enabled);
        Assert.Empty(await RemindersTo(s.AnnaEmail));
    }

    [Fact]
    public async Task Admin_sets_the_reminder_hours()
    {
        var s = await CreateAsync();

        var invalid = await s.Business.Owner.PutAsJsonAsync("/api/tenancy/tenants/current/settings",
            new UpdateTenantSettingsRequest(FirstReminderHours: 500), TestJson.Options);
        var saved = await s.Business.Owner.PutAsJsonAsync("/api/tenancy/tenants/current/settings",
            new UpdateTenantSettingsRequest(FirstReminderHours: 48, SecondReminderHours: 0), TestJson.Options);
        var tenant = (await saved.Content.ReadFromJsonAsync<TenantDto>(TestJson.Options))!;
        var appointment = await BookAsync(s, 10);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal((48, 0, 2), (tenant.FirstReminderHours, tenant.SecondReminderHours, tenant.CancellationNoticeHours));
        Assert.Equal([48], ScheduledReminders(appointment.Id).Select(j => j.Hours));
    }
}
