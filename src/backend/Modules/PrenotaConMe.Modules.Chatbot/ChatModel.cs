using System.Text.Json.Serialization;
using OpenAI.Chat;

namespace PrenotaConMe.Modules.Chatbot;

public enum ChatTurnRole
{
    User,
    Assistant,
    Tool,
}

/// <summary>A function call the model asked for; <paramref name="Arguments"/> is a JSON object.</summary>
public sealed record ToolCall(string Id, string Name, string Arguments);

/// <summary>
/// One entry of a conversation, stored as JSON: client text, assistant text or tool calls, or a tool result
/// (<see cref="ToolCallId"/> + <see cref="Text"/>). The system prompt is not stored: it is rebuilt on every message.
/// </summary>
public sealed record ChatTurn(
    ChatTurnRole Role,
    string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ToolCall>? ToolCalls = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolCallId = null);

/// <summary>A function the model may call; <paramref name="Parameters"/> is a JSON schema.</summary>
public sealed record ChatToolDefinition(string Name, string Description, string Parameters);

/// <summary>Either text for the client or tool calls to run before asking the model again.</summary>
public sealed record ChatModelReply(string? Text, IReadOnlyList<ToolCall> ToolCalls);

/// <summary>The language model behind the chatbot (OpenAI; a scripted fake in tests).</summary>
public interface IChatModel
{
    Task<ChatModelReply> CompleteAsync(
        string systemPrompt, IReadOnlyList<ChatTurn> conversation, IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken);
}

public sealed class ChatbotOptions
{
    /// <summary>Without a key the chatbot is off.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Chat model with function calling.</summary>
    public string Model { get; set; } = "gpt-4.1-mini";
}

/// <summary>OpenAI chat completions with function calling.</summary>
internal sealed class OpenAiChatModel(ChatbotOptions options) : IChatModel
{
    private readonly Lazy<ChatClient> _client = new(() => new ChatClient(options.Model, options.ApiKey));

    public async Task<ChatModelReply> CompleteAsync(
        string systemPrompt, IReadOnlyList<ChatTurn> conversation, IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage> { new SystemChatMessage(systemPrompt) };
        messages.AddRange(conversation.Select(ToMessage));

        var completionOptions = new ChatCompletionOptions();
        foreach (var tool in tools)
        {
            completionOptions.Tools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.Parameters)));
        }

        ChatCompletion completion = await _client.Value.CompleteChatAsync(messages, completionOptions, cancellationToken);
        var calls = completion.ToolCalls
            .Select(c => new ToolCall(c.Id, c.FunctionName, c.FunctionArguments.ToString()))
            .ToList();
        var text = string.Concat(completion.Content.Where(p => p.Kind == ChatMessageContentPartKind.Text).Select(p => p.Text));
        return new ChatModelReply(string.IsNullOrWhiteSpace(text) ? null : text, calls);
    }

    private static ChatMessage ToMessage(ChatTurn turn) => turn.Role switch
    {
        ChatTurnRole.User => new UserChatMessage(turn.Text ?? string.Empty),
        ChatTurnRole.Tool => new ToolChatMessage(turn.ToolCallId, turn.Text ?? string.Empty),
        _ when turn.ToolCalls is { Count: > 0 } => new AssistantChatMessage(turn.ToolCalls
            .Select(c => ChatToolCall.CreateFunctionToolCall(c.Id, c.Name, BinaryData.FromString(c.Arguments)))
            .ToList()),
        _ => new AssistantChatMessage(turn.Text ?? string.Empty),
    };
}
