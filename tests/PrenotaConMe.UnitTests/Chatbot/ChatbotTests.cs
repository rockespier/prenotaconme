using PrenotaConMe.Modules.Chatbot;
using PrenotaConMe.Modules.Chatbot.Domain;

namespace PrenotaConMe.UnitTests.Chatbot;

public sealed class ChatbotTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_action_is_confirmed_only_by_a_later_client_message()
    {
        var user = Guid.NewGuid();
        var conversation = ChatConversation.Start("telegram", "1", user);
        conversation.Receive(user, "Un taglio alle 10", Now);
        conversation.Propose("book-10");

        var sameMessage = conversation.IsConfirmed("book-10");
        conversation.Receive(user, "sì", Now.AddMinutes(1));

        Assert.False(sameMessage);
        Assert.False(conversation.IsConfirmed("book-11"));
        Assert.True(conversation.IsConfirmed("book-10"));
    }

    [Fact]
    public void Conversation_starts_over_after_24_hours_or_for_another_user()
    {
        var user = Guid.NewGuid();
        var conversation = ChatConversation.Start("telegram", "1", user);
        conversation.Save(conversation.Receive(user, "ciao", Now));
        conversation.Propose("book-10");

        var nextDay = conversation.Receive(user, "di nuovo", Now + ChatConversation.Lifetime + TimeSpan.FromMinutes(1));
        conversation.Save(nextDay);
        var otherUser = conversation.Receive(Guid.NewGuid(), "sono un altro", Now.AddDays(1).AddHours(1));

        Assert.Single(nextDay);
        Assert.False(conversation.IsConfirmed("book-10"));
        Assert.Single(otherUser);
    }

    [Fact]
    public void Old_messages_are_dropped_from_a_client_message_so_tool_results_keep_their_call()
    {
        var user = Guid.NewGuid();
        var conversation = ChatConversation.Start("telegram", "1", user);
        var turns = new List<ChatTurn>();
        for (var i = 0; i < 15; i++)
        {
            turns.Add(new ChatTurn(ChatTurnRole.User, $"m{i}"));
            turns.Add(new ChatTurn(ChatTurnRole.Assistant, null, [new ToolCall($"c{i}", "list_services", "{}")]));
            turns.Add(new ChatTurn(ChatTurnRole.Tool, "{}", ToolCallId: $"c{i}"));
        }

        conversation.Save(turns);
        var kept = conversation.ReadMessages();

        Assert.True(kept.Count <= ChatConversation.MaxTurns);
        Assert.Equal(ChatConversation.MaxClientMessages, kept.Count(t => t.Role == ChatTurnRole.User));
        Assert.Equal(ChatTurnRole.User, kept[0].Role);
        Assert.Equal("m14", kept[^3].Text);
        Assert.Equal("c14", kept[^1].ToolCallId);
    }

    [Fact]
    public void A_chat_over_the_minute_limit_is_answered_once_then_ignored_until_the_next_minute()
    {
        var conversation = ChatConversation.Start("telegram", "1", Guid.NewGuid());
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var results = Enumerable.Range(0, 5).Select(i => conversation.Count(Now.AddSeconds(i), today, 3, 100)).ToList();
        var nextMinute = conversation.Count(Now.AddMinutes(1), today, 3, 100);

        Assert.Equal([ChatRate.Allowed, ChatRate.Allowed, ChatRate.Allowed, ChatRate.TooFast, ChatRate.Ignore], results);
        Assert.Equal(ChatRate.Allowed, nextMinute);
    }

    [Fact]
    public void A_chat_over_the_daily_limit_waits_for_the_next_day()
    {
        var conversation = ChatConversation.Start("telegram", "1", Guid.NewGuid());
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var results = Enumerable.Range(0, 4).Select(i => conversation.Count(Now.AddMinutes(i * 5), today, 10, 2)).ToList();
        var tomorrow = conversation.Count(Now.AddDays(1), today.AddDays(1), 10, 2);

        Assert.Equal([ChatRate.Allowed, ChatRate.Allowed, ChatRate.DailyLimit, ChatRate.Ignore], results);
        Assert.Equal(ChatRate.Allowed, tomorrow);
    }

    [Fact]
    public void A_handoff_ends_when_resumed_or_after_24_hours_even_if_the_client_keeps_writing()
    {
        var user = Guid.NewGuid();
        var conversation = ChatConversation.Start("telegram", "1", user);
        conversation.Save(conversation.Receive(user, "operatore", Now));

        var first = conversation.Handoff("operatore", Now);
        var again = conversation.Handoff("di nuovo", Now.AddMinutes(1));
        for (var h = 1; h <= 24; h++)
        {
            conversation.Save(conversation.Receive(user, "ci sei?", Now.AddHours(h)));
        }

        var stillPaused = conversation.IsHandedOff;
        conversation.Receive(user, "allora?", Now.AddHours(24).AddMinutes(1));

        Assert.True(first);
        Assert.False(again);
        Assert.True(stillPaused);
        Assert.False(conversation.IsHandedOff);
    }

    [Theory]
    [InlineData("Voglio parlare con un operatore", true)]
    [InlineData("Posso parlare con una persona?", true)]
    [InlineData("quiero hablar con un humano", true)]
    [InlineData("Can I talk to someone?", true)]
    [InlineData("Una reserva para una persona", false)]
    [InlineData("Vorrei un taglio", false)]
    public void Asking_for_a_person_is_recognized_without_the_model(string text, bool expected) =>
        Assert.Equal(expected, QuickReplies.AsksForPerson(text));

    [Theory]
    [InlineData("Ciao!", "Ciao Anna!")]
    [InlineData("buenos días 👋", "¡Hola Anna!")]
    [InlineData("Hello", "Hi Anna!")]
    [InlineData("grazie mille", "Prego!")]
    public void Bare_greetings_and_thanks_get_a_fixed_reply_in_their_language(string text, string start) =>
        Assert.StartsWith(start, QuickReplies.For(text, "Anna Rossi"), StringComparison.Ordinal);

    [Theory]
    [InlineData("Ciao, posso prenotare?")]
    [InlineData("sì")]
    [InlineData("ok")]
    [InlineData("hola quiero una cita")]
    public void Other_messages_go_to_the_model(string text) =>
        Assert.Null(QuickReplies.For(text, "Anna"));

    [Theory]
    [InlineData("2026-12-02T10:30", "2026-12-02T09:30:00+00:00")]
    [InlineData("2026-07-01 10:30", "2026-07-01T08:30:00+00:00")]
    [InlineData("2026-12-02T10:30:00+01:00", "2026-12-02T09:30:00+00:00")]
    public void Local_times_are_read_in_the_business_time_zone(string value, string utc) =>
        Assert.Equal(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture),
            BookingTools.ToInstant(value, "Europe/Rome")!.Value.Utc);

    [Theory]
    [InlineData("2026-03-29T02:30")] // skipped by the change to summer time
    [InlineData("tomorrow at 10")]
    public void Invalid_or_skipped_times_are_refused(string value) =>
        Assert.Null(BookingTools.ToInstant(value, "Europe/Rome"));
}
