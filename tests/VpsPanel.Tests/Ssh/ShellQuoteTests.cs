using VpsPanel.Ssh;

namespace VpsPanel.Tests.Ssh;

public sealed class ShellQuoteTests
{
    [Theory(DisplayName = "Arbitrary string => string in single quotes with escaped quotes")]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("", "''")]
    [InlineData("$HOME `id`", "'$HOME `id`'")]
    public void Q_Should_WrapInSingleQuotes_When_Called(string value, string expected)
    {
        // Arrange
        var input = value;

        // Act
        var quoted = ShellQuote.Q(input);

        // Assert
        quoted.Should().Be(expected);
    }
}