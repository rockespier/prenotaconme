using System.Collections.Concurrent;
using PrenotaConMe.Modules.Notifications;

namespace PrenotaConMe.IntegrationTests;

/// <summary>Replaces SMTP in tests: keeps every e-mail the app sends.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Waits (sending happens in the background) until <paramref name="count"/> matching e-mails arrived.</summary>
    public async Task<IReadOnlyList<EmailMessage>> WaitForAsync(Func<EmailMessage, bool> match, int count)
    {
        for (var i = 0; i < 100; i++)
        {
            var found = _sent.Where(match).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(50);
        }

        return _sent.Where(match).ToList();
    }
}
