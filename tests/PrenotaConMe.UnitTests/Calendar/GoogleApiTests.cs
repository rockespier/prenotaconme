using System.Text;
using PrenotaConMe.Modules.Calendar;

namespace PrenotaConMe.UnitTests.Calendar;

public sealed class GoogleApiTests
{
    private static string Part(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void The_email_is_read_from_the_id_token_payload() =>
        Assert.Equal("mario@gmail.com", HttpGoogleCalendarApi.EmailOf($"{Part("{\"alg\":\"RS256\"}")}.{Part("{\"email\":\"mario@gmail.com\",\"sub\":\"1\"}")}.sig"));

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public void Missing_or_malformed_tokens_have_no_email(string? token) =>
        Assert.Null(HttpGoogleCalendarApi.EmailOf(token));
}
