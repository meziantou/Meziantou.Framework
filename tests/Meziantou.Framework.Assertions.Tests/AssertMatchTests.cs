using System.Text.RegularExpressions;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertMatchTests
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void MatchRegex_Success()
    {
        var regex = new Regex("^sam", RegexOptions.CultureInvariant, RegexMatchTimeout);
        var actual = "sample";

        AssertionsAssert.Matches(regex, actual);
    }

    [Fact]
    public void MatchRegex_Fails()
    {
        var regex = new Regex("^sam", RegexOptions.CultureInvariant, RegexMatchTimeout);
        var actual = "value";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Matches(regex, actual), """
            Assert.Match() assertion failed.
            Expected expression: regex
            Actual expression:   actual
            Expected pattern: "^sam"
            Actual:           "value"
            """);
    }

    [Fact]
    public void MatchPattern_Success()
    {
        var pattern = "^sam";
        var actual = "sample";

        AssertionsAssert.Matches(pattern, actual);
    }

    [Fact]
    public void MatchPattern_Fails()
    {
        var pattern = "^sam";
        var actual = "value";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Matches(pattern, actual), """
            Assert.Match() assertion failed.
            Expected expression: pattern
            Actual expression:   actual
            Expected pattern: "^sam"
            Actual:           "value"
            """);
    }

    [Fact]
    public void DoesNotMatchRegex_Success()
    {
        var regex = new Regex("^sam", RegexOptions.CultureInvariant, RegexMatchTimeout);
        var actual = "value";

        AssertionsAssert.DoesNotMatch(regex, actual);
    }

    [Fact]
    public void DoesNotMatchRegex_Fails()
    {
        var regex = new Regex("^sam", RegexOptions.CultureInvariant, RegexMatchTimeout);
        var actual = "sample";

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotMatch(regex, actual), """
            Assert.DoesNotMatch() assertion failed.
            Expected expression: regex
            Actual expression:   actual
            Not expected pattern: "^sam"
            Actual:               "sample"
            """);
    }

    [Fact]
    public void DoesNotMatchPattern_Success()
    {
        var pattern = "^sam";
        var actual = "value";

        AssertionsAssert.DoesNotMatch(pattern, actual);
    }

    [Fact]
    public void DoesNotMatchPattern_Fails()
    {
        var pattern = "^sam";
        var actual = "sample";

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotMatch(pattern, actual), """
            Assert.DoesNotMatch() assertion failed.
            Expected expression: pattern
            Actual expression:   actual
            Not expected pattern: "^sam"
            Actual:               "sample"
            """);
    }
}
