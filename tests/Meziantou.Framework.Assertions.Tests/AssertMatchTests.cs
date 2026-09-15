using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed partial class AssertMatchTests
{
    [Fact]
    public void MatchRegex_Success()
    {
        var regex = SamplePrefixRegex();
        var actual = "sample";

        AssertionsAssert.Matches(regex, actual);
    }

    [Fact]
    public void MatchRegex_Fails()
    {
        var regex = SamplePrefixRegex();
        var actual = "value";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Matches(regex, actual), """
            Assert.Matches() assertion failed.
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
            Assert.Matches() assertion failed.
            Expected expression: pattern
            Actual expression:   actual
            Expected pattern: "^sam"
            Actual:           "value"
            """);
    }

    [Fact]
    public void DoesNotMatchRegex_Success()
    {
        var regex = SamplePrefixRegex();
        var actual = "value";

        AssertionsAssert.DoesNotMatch(regex, actual);
    }

    [Fact]
    public void DoesNotMatchRegex_Fails()
    {
        var regex = SamplePrefixRegex();
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

    [Fact]
    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout", Justification = "The test checks the default timeout")]
    public void Pattern_UsesTheDefaultMatchTimeout()
    {
        // Guards against a regular expression created before the module initializer, as the pattern below would then run for hours
        AssertionsAssert.Equal(DefaultMatchTimeout, new Regex("^sam", RegexOptions.None).MatchTimeout);

        const string CatastrophicPattern = "^(a|aa)+$";
        var actual = new string('a', 100) + "!";

        AssertionsAssert.Equal(DefaultMatchTimeout, AssertionsAssert.Throws<RegexMatchTimeoutException>(() => AssertionsAssert.Matches(CatastrophicPattern, actual)).MatchTimeout);
        AssertionsAssert.Equal(DefaultMatchTimeout, AssertionsAssert.Throws<RegexMatchTimeoutException>(() => AssertionsAssert.DoesNotMatch(CatastrophicPattern, actual)).MatchTimeout);
    }

    // Matches(string pattern, ...) must behave like Regex.IsMatch(input, pattern), which uses the default match timeout.
    // The default timeout is read once per process, before the first regular expression is created, so it is set when
    // the test assembly is loaded.
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(500);

    [ModuleInitializer]
    internal static void InitializeRegexDefaultMatchTimeout()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", DefaultMatchTimeout);
    }

    [GeneratedRegex("^sam", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SamplePrefixRegex();
}
