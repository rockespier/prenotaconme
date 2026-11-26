using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PrenotaConMe.Infrastructure.Persistence;
using PrenotaConMe.Modules.Chatbot.Domain;
using PrenotaConMe.Shared.Auth;
using PrenotaConMe.Shared.Tenancy;

namespace PrenotaConMe.Modules.Chatbot;

/// <param name="FromClient">False for the assistant's replies.</param>
public sealed record HandoffMessageDto(bool FromClient, string Text);

/// <param name="Messages">The recent chat, oldest first (tool calls left out).</param>
public sealed record HandoffDto(
    Guid Id, string Channel, DateTimeOffset HandoffAt, string? Reason, string? ClientName, string? ClientPhone,
    string? ClientEmail, IReadOnlyList<HandoffMessageDto> Messages);

/// <summary>F3-06: chats handed to a person, for the Admin to call the client back and turn the assistant on again.</summary>
internal static class HandoffEndpoints
{
    /// <summary>Messages shown per chat.</summary>
    public const int MaxMessages = 20;

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapGet("/handoffs", ListAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("ListHandoffs");

        group.MapPost("/handoffs/{id:guid}/resume", ResumeAsync)
            .RequireTenantRole(TenantRole.Admin)
            .WithName("ResumeHandoff");
    }

    private static async Task<Ok<List<HandoffDto>>> ListAsync(
        AppDbContext db, IUserDirectory users, TimeProvider clock, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - ChatConversation.Lifetime;
        var conversations = await db.Set<ChatConversation>().AsNoTracking()
            .Where(c => c.HandoffAt != null && c.HandoffAt > since)
            .OrderByDescending(c => c.HandoffAt)
            .ToListAsync(ct);
        var clients = await users.GetByIdsAsync(conversations.Select(c => c.UserId).Distinct().ToList(), ct);

        return TypedResults.Ok(conversations.Select(c =>
        {
            var client = clients.GetValueOrDefault(c.UserId);
            var messages = c.ReadMessages()
                .Where(t => t.Role != ChatTurnRole.Tool && t.ToolCalls is not { Count: > 0 } && !string.IsNullOrWhiteSpace(t.Text))
                .Select(t => new HandoffMessageDto(t.Role == ChatTurnRole.User, t.Text!))
                .TakeLast(MaxMessages)
                .ToList();
            return new HandoffDto(
                c.Id, c.Channel, c.HandoffAt!.Value, c.HandoffReason, client?.DisplayName, client?.PhoneNumber, client?.Email, messages);
        }).ToList());
    }

    private static async Task<Results<NoContent, NotFound>> ResumeAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var conversation = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is not { IsHandedOff: true })
        {
            return TypedResults.NotFound();
        }

        conversation.Resume();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
