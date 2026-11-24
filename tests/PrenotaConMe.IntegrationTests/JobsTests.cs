using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Modules.Users;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.IntegrationTests;

/// <summary>F2-01: Hangfire jobs keep the tenant that queued them; the dashboard is for platform admins only.</summary>
public sealed class JobsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Job_runs_in_the_tenant_that_queued_it()
    {
        var tenantId = Guid.NewGuid();
        var withTenant = Guid.NewGuid();
        var platform = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
            scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>().Enqueue<TenantProbeJob>(j => j.Run(withTenant));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>().Enqueue<TenantProbeJob>(j => j.Run(platform));
        }

        Assert.Equal(tenantId, await TenantProbeJob.WaitForAsync(withTenant));
        Assert.Null(await TenantProbeJob.WaitForAsync(platform));
    }

    [Fact]
    public async Task Dashboard_requires_a_platform_admin_session()
    {
        var anonymous = factory.CreateClient();
        var someone = factory.CreateUserClient($"user-{Guid.NewGuid():N}");
        var root = factory.CreateUserClient(ApiFactory.PlatformAdminUid);

        var anonymousPage = await anonymous.GetAsync(new Uri("/hangfire", UriKind.Relative));
        var someoneSession = await someone.PostAsync(new Uri("/api/jobs/dashboard-session", UriKind.Relative), null);
        var someoneMe = await someone.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);
        var rootMe = await root.GetFromJsonAsync<MeDto>("/api/users/me", TestJson.Options);
        var rootSession = await root.PostAsync(new Uri("/api/jobs/dashboard-session", UriKind.Relative), null);
        var rootPage = await root.GetAsync(new Uri("/hangfire", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, someoneSession.StatusCode);
        Assert.False(someoneMe!.IsPlatformAdmin);
        Assert.True(rootMe!.IsPlatformAdmin);
        Assert.Equal(HttpStatusCode.NoContent, rootSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rootPage.StatusCode);
    }
}

public sealed class TenantProbeJob(ITenantContext tenant)
{
    private static readonly ConcurrentDictionary<Guid, Guid?> Seen = new();

    public void Run(Guid probeId) => Seen[probeId] = tenant.TenantId;

    public static async Task<Guid?> WaitForAsync(Guid probeId)
    {
        for (var i = 0; i < 200; i++)
        {
            if (Seen.TryGetValue(probeId, out var tenantId))
            {
                return tenantId;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Job {probeId} did not run.");
    }
}
