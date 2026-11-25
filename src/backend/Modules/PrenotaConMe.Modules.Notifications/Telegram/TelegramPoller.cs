using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Notifications.Domain;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Notifications.Telegram;

/// <summary>
/// Polling mode (local development, no public URL): long-polls getUpdates for every business bot and hands each
/// message to <see cref="TelegramUpdateHandler"/> in that bot's tenant.
/// </summary>
internal sealed partial class TelegramPoller(IServiceScopeFactory scopes, ITelegramApi api, IDataProtectionProvider protection,
    ILogger<TelegramPoller> logger) : BackgroundService
{
    private const int TimeoutSeconds = 20;
    private readonly ConcurrentDictionary<Guid, long> _offsets = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var protector = protection.CreateProtector(TelegramService.TokenPurpose);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<TelegramBot> bots;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    bots = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<TelegramBot>()
                        .IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking().ToListAsync(stoppingToken);
                }

                if (bots.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                await Task.WhenAll(bots.Select(b => PollAsync(b, protector.Unprotect(b.ProtectedToken), stoppingToken)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPollFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PollAsync(TelegramBot bot, string token, CancellationToken ct)
    {
        var updates = await api.GetUpdatesAsync(token, _offsets.GetValueOrDefault(bot.Id), TimeoutSeconds, ct);
        foreach (var raw in updates)
        {
            var update = TelegramUpdate.Parse(raw);
            _offsets[bot.Id] = update.UpdateId + 1;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(bot.TenantId);
                await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(bot, update, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip a message that cannot be handled rather than retrying it forever.
                LogHandleFailed(logger, ex, update.UpdateId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram polling failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Telegram update {UpdateId} could not be handled")]
    private static partial void LogHandleFailed(ILogger logger, Exception exception, long updateId);
}
