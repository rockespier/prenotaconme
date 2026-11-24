using PrenotaConMe.Modules.Users;

namespace PrenotaConMe.UnitTests.Users;

public sealed class PhoneNumbersTests
{
    [Theory]
    [InlineData("+39 333 123 4567", "+393331234567")]
    [InlineData("0039 333-123-4567", "+393331234567")]
    [InlineData("+34 (612) 34.56.78", "+34612345678")]
    public void Normalizes_international_numbers(string input, string expected) =>
        Assert.Equal(expected, PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData("333 123 4567")] // no country code
    [InlineData("+0 333 123 4567")]
    [InlineData("+39 abc")]
    [InlineData("+39 12")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_numbers(string? input) => Assert.Null(PhoneNumbers.Normalize(input));
}
