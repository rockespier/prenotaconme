using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrenotaConMe.Infrastructure.Jobs;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Shared.Privacy;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>F5-04: section <c>Privacy</c>.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Chat conversations untouched for longer are deleted (the bot only uses the last 24 hours anyway).</summary>
    public int ChatRetentionDays { get; set; } = 30;
}

/// <summary>F5-04: the user's chats with the assistant (deleted with the account by cascade).</summary>
internal sealed class ChatbotPersonalData(AppDbContext db, ITenantDirectory tenants) : IPersonalData
{
    public string Section => "chats";

    public async Task<object?> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var chats = await db.Set<ChatConversation>().IgnoreQueryFilters([AppDbContext.TenantFilter]).AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.TenantId, c.Channel, c.LastMessageAt, c.Messages, c.HandoffAt, c.HandoffReason })
            .ToListAsync(cancellationToken);
        if (chats.Count == 0)
        {
            return null;
        }

        var businesses = await tenants.GetByIdsAsync(chats.Select(c => c.TenantId).Distinct().ToList(), cancellationToken);
        return chats.Select(c => new
        {
            Business = businesses.TryGetValue(c.TenantId, out var b) ? b.Name : null,
            c.Channel,
            c.LastMessageAt,
            Messages = JsonDocument.Parse(c.Messages).RootElement.Clone(),
            c.HandoffAt,
            c.HandoffReason,
        }).ToList();
    }

    public Task EraseAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>F5-04: daily clean-up of old chats, in every business.</summary>
public sealed class ChatRetentionJob(AppDbContext db, IOptions<PrivacyOptions> options, TimeProvider clock)
{
    public const string Id = "privacy-chat-retention";

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().AddDays(-options.Value.ChatRetentionDays);
        return db.Set<ChatConversation>().IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(c => c.LastMessageAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

internal sealed class ChatRetentionRegistration : IRecurringJobRegistration
{
    public void Register(IRecurringJobManager jobs) =>
        jobs.AddOrUpdate<ChatRetentionJob>(ChatRetentionJob.Id, j => j.RunAsync(CancellationToken.None), Cron.Daily(3));
}
