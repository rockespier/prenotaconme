using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Infrastructure.Jobs;

/// <summary>
/// Scoped job client: stamps the current tenant on every job it creates (job parameter "TenantId"), so
/// <see cref="TenantJobActivator"/> can restore it when the job runs. Without a tenant, jobs run platform-level.
/// </summary>
internal sealed class TenantBackgroundJobClient(BackgroundJobClient inner, ITenantContext tenant) : IBackgroundJobClientV2
{
    public const string TenantParameter = "TenantId";

    public JobStorage Storage => inner.Storage;

    public string Create(Job job, IState state) => Create(job, state, null);

    public string Create(Job job, IState state, IDictionary<string, object>? parameters)
    {
        var all = parameters is null ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters);
        if (tenant.TenantId is { } id)
        {
            all[TenantParameter] = id;
        }

        return inner.Create(job, state, all);
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => inner.ChangeState(jobId, state, expectedState);
}

/// <summary>One DI scope per job run, with the tenant of the job (if any) set on <see cref="TenantContext"/>.</summary>
internal sealed class TenantJobActivator(IServiceScopeFactory scopes) : JobActivator
{
    public override object ActivateJob(Type jobType) =>
        throw new InvalidOperationException("Jobs are activated inside a scope.");

    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        var scope = scopes.CreateAsyncScope();
        if (context.GetJobParameter<Guid?>(TenantBackgroundJobClient.TenantParameter) is { } tenantId)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        }

        return new Scope(scope);
    }

    private sealed class Scope(AsyncServiceScope scope) : JobActivatorScope
    {
        public override object Resolve(Type type) => ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, type);

        public override void DisposeScope() => scope.Dispose();
    }
}
