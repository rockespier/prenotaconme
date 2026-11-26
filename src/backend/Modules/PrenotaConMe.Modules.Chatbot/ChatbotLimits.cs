using System.Diagnostics.Metrics;

namespace PrenotaConMe.Modules.Chatbot;

/// <summary>F3-07: abuse and cost limits of the chatbot (configuration section "Chatbot").</summary>
public sealed class ChatbotLimits
{
    /// <summary>Messages one chat may send per minute; the first one over gets "wait a minute", the rest no answer.</summary>
    public int MessagesPerMinute { get; set; } = 6;

    /// <summary>Messages one chat may send per business-local day.</summary>
    public int MessagesPerDay { get; set; } = 60;

    /// <summary>Messages the model may answer per business and day.</summary>
    public int TenantMessagesPerDay { get; set; } = 500;

    /// <summary>Input + output tokens per business and day (about 1-2 USD a day with gpt-4.1-mini).</summary>
    public long TenantTokensPerDay { get; set; } = 2_000_000;

    /// <summary>Minutes of voice messages transcribed per business and day (about 0.20 USD with gpt-4o-mini-transcribe).</summary>
    public int TenantAudioMinutesPerDay { get; set; } = 60;
}

/// <summary>F3-07: chatbot use as OpenTelemetry metrics (meter <see cref="MeterName"/>, exported to Seq).</summary>
internal sealed class ChatbotMetrics
{
    public const string MeterName = "PrenotaConMe.Chatbot";

    private readonly Counter<long> _messages;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _audio;

    public ChatbotMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _messages = meter.CreateCounter<long>("chatbot.messages", "{message}", "Client messages by outcome");
        _tokens = meter.CreateCounter<long>("chatbot.tokens", "{token}", "Language-model tokens by kind");
        _audio = meter.CreateCounter<long>("chatbot.audio", "s", "Seconds of voice messages transcribed");
    }

    /// <param name="outcome">answered, quick_reply, too_fast, daily_limit, ignored, budget_exceeded, voice_refused or error.</param>
    public void Message(string tenant, string outcome) =>
        _messages.Add(1, new KeyValuePair<string, object?>("tenant", tenant), new KeyValuePair<string, object?>("outcome", outcome));

    public void Audio(string tenant, long seconds) => _audio.Add(seconds, new KeyValuePair<string, object?>("tenant", tenant));

    public void Tokens(string tenant, long input, long output)
    {
        var tag = new KeyValuePair<string, object?>("tenant", tenant);
        _tokens.Add(input, tag, new KeyValuePair<string, object?>("kind", "input"));
        _tokens.Add(output, tag, new KeyValuePair<string, object?>("kind", "output"));
    }
}
