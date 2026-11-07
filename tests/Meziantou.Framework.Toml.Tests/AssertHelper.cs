using System;

namespace Meziantou.Framework.Toml.Tests;

public static class AssertHelper
{
    public static void AreEqualNormalizeNewLine(string expected, string actual, bool alwaysDisplay = false, string? message = null)
    {
        expected = NormalizeEndOfLine(expected);
        actual = NormalizeEndOfLine(actual);
        if (alwaysDisplay || expected != actual)
        {
            StandardTests.DisplayHeader("Actual");
            TestContext.Current.TestOutputHelper?.WriteLine(actual);
            StandardTests.DisplayHeader("Expected");
            TestContext.Current.TestOutputHelper?.WriteLine(expected);
        }
        Assert.Equal(expected, actual, message: message ?? string.Empty);
    }

    public static string NormalizeEndOfLine(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

}
