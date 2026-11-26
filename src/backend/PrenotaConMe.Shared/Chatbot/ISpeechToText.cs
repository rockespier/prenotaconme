namespace PrenotaConMe.Shared.Chatbot;

/// <summary>F3-04: turns a client's voice message into text for the chatbot (implemented by the Chatbot module).</summary>
public interface ISpeechToText
{
    /// <summary>Longest voice message transcribed (cost and latency limit).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(1);

    /// <param name="fileName">With the format's extension (e.g. "voice.ogg"): the provider uses it to read the audio.</param>
    /// <returns>The text, or null when nothing could be understood.</returns>
    Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken);
}
