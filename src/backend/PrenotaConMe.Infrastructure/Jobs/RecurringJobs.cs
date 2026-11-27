using Hangfire;
using Microsoft.Extensions.Hosting;

namespace PrenotaConMe.Infrastructure.Jobs;

/// <summary>
/// A recurring Hangfire job a module wants (e.g. F5-04 data retention). Registered with
/// <c>services.AddSingleton&lt;IRecurringJobRegistration, ...&gt;()</c>; added or updated at startup in processes that
/// run jobs. Recurring jobs run with no tenant: their code must ignore the tenant filter on purpose.
/// </summary>
public interface IRecurringJobRegistration
{
    void Register(IRecurringJobManager jobs);
}

internal sealed class RecurringJobsRegistrar(IEnumerable<IRecurringJobRegistration> registrations, IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registrations)
        {
            registration.Register(jobs);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
