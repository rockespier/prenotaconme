using PrenotaConMe.Shared.Chatbot;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces OpenAI speech-to-text in tests: the "audio" is the text itself (see FakeTelegramApi.DownloadFileAsync).</summary>
public sealed class FakeSpeechToText : ISpeechToText
{
    /// <summary>File ids starting with this are treated as audio nobody could understand.</summary>
    public const string Unreadable = "unreadable";

    private int _calls;

    /// <summary>Transcriptions so far (F3-07: refused voice messages must not be transcribed).</summary>
    public int Calls => _calls;

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        using var reader = new StreamReader(audio);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.StartsWith(Unreadable, StringComparison.Ordinal) ? null : text;
    }
}
