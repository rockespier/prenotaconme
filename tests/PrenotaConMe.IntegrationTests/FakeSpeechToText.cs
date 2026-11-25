using PrenotaConMe.Shared.Chatbot;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces Whisper in tests: the "audio" is the text itself (see FakeTelegramApi.DownloadFileAsync).</summary>
public sealed class FakeSpeechToText : ISpeechToText
{
    /// <summary>File ids starting with this are treated as audio nobody could understand.</summary>
    public const string Unreadable = "unreadable";

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(audio);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.StartsWith(Unreadable, StringComparison.Ordinal) ? null : text;
    }
}
