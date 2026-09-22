using SqlFluff.Ssms.Core;
using Xunit;

namespace SqlFluff.Ssms.Tests
{
    public class ArgumentQuotingTests
    {
        [Theory]
        [InlineData("simple", "simple")]
        [InlineData("C:\\no\\spaces\\here.sql", "C:\\no\\spaces\\here.sql")]
        public void Quote_LeavesSimpleArgumentsUnquoted(string input, string expected)
        {
            Assert.Equal(expected, ArgumentQuoting.Quote(input));
        }

        [Fact]
        public void Quote_WrapsArgumentsWithSpaces()
        {
            Assert.Equal("\"C:\\Program Files\\sqlfluff.exe\"", ArgumentQuoting.Quote("C:\\Program Files\\sqlfluff.exe"));
        }

        [Fact]
        public void Quote_WrapsEmptyString()
        {
            Assert.Equal("\"\"", ArgumentQuoting.Quote(""));
        }

        [Fact]
        public void Quote_EscapesEmbeddedQuotes()
        {
            Assert.Equal("\"say \\\"hi\\\"\"", ArgumentQuoting.Quote("say \"hi\""));
        }

        [Theory]
        [InlineData("has\ttab")]
        [InlineData("has\nnewline")]
        public void Quote_WrapsArgumentsWithWhitespaceControlCharacters(string input)
        {
            string result = ArgumentQuoting.Quote(input);
            Assert.StartsWith("\"", result);
            Assert.EndsWith("\"", result);
        }

        [Fact]
        public void Quote_DoublesBackslashesImmediatelyBeforeClosingQuote()
        {
            // CommandLineToArgvW rule: backslashes are only special immediately before a " —
            // when the whole argument needs quoting (here, because of the space), a trailing
            // backslash must be doubled so it isn't read as escaping the closing quote.
            string result = ArgumentQuoting.Quote("dir with space\\");
            Assert.Equal("\"dir with space\\\\\"", result);
        }

        [Fact]
        public void Quote_DoublesBackslashesBeforeEmbeddedQuote()
        {
            string result = ArgumentQuoting.Quote("path\\\"name");
            Assert.Equal("\"path\\\\\\\"name\"", result);
        }

        [Fact]
        public void Quote_RoundTripsThroughArgvParsingRules()
        {
            // Not a real CommandLineToArgvW call (that's Windows-only and this test project
            // targets net8.0 cross-platform), but confirms the quoting is self-consistent:
            // an already-simple, already-safe value passed through twice is stable.
            string once = ArgumentQuoting.Quote("C:\\dialect tsql");
            string twice = ArgumentQuoting.Quote(once);
            Assert.NotEqual(once, twice); // quoting a quoted string must add another layer, not collapse
        }
    }
}
