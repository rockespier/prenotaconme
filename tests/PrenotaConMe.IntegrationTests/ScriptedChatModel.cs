using System.Collections.Concurrent;
using System.Text.Json;
using PrenotaConMe.Modules.Chatbot;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces OpenAI in tests: answers each model call with the next scripted reply and records what it got.</summary>
public sealed class ScriptedChatModel : IChatModel
{
    private readonly ConcurrentQueue<ChatModelReply> _replies = new();

    public sealed record Call(string SystemPrompt, IReadOnlyList<ChatTurn> Conversation);

    public ConcurrentQueue<Call> Calls { get; } = new();

    public void Script(params ChatModelReply[] replies)
    {
        foreach (var reply in replies)
        {
            _replies.Enqueue(reply);
        }
    }

    public static ChatModelReply Tool(string name, object arguments) =>
        new(null, [new ToolCall($"call_{Guid.NewGuid():N}", name, JsonSerializer.Serialize(arguments))]);

    public static ChatModelReply Say(string text) => new(text, []);

    public Task<ChatModelReply> CompleteAsync(
        string systemPrompt, IReadOnlyList<ChatTurn> conversation, IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        Calls.Enqueue(new Call(systemPrompt, conversation.ToList()));
        return Task.FromResult(_replies.TryDequeue(out var reply) ? reply : Say("(no script)"));
    }

    /// <summary>Tool results the engine sent back to the model, in order.</summary>
    public IReadOnlyList<string> ToolResults() =>
        Calls.LastOrDefault()?.Conversation.Where(t => t.Role == ChatTurnRole.Tool).Select(t => t.Text!).ToList() ?? [];
}
