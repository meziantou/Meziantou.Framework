using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class InlineDiffAssertionMessageFormatterTests
{
    private static void Test(string left, string right, string expected)
    {
        var message = InlineDiffAssertionMessageFormatter.Instance.FormatMessage(left, right);
        message = message.ReplaceLineEndings("\n");
        const string ExpectedStartString = "- Snapshot\n+ Received\n\n\n";
        Assert.StartsWith(ExpectedStartString, message, StringComparison.Ordinal);
        Assert.Equal(expected, message[ExpectedStartString.Length..]);
    }

    [Fact]
    public void SameValue()
    {
        Test("text", "text", "  text");
    }

    [Fact]
    public void SameValue_MultiLines()
    {
        Test("line1\nline2", "line1\nline2", "  line1\n  line2");
    }

    [Fact]
    public void SingleLine()
    {
        Test("old", "new", "- old\n+ new");
    }

    [Fact]
    public void Multilines()
    {
        Test("line1\nline2\nline3", "line1\nline2\nline3_new", "  line1\n  line2\n- line3\n+ line3_new");
    }

    [Fact]
    public void Multilines2()
    {
        Test("line1\nline2\nline3\nline4", "line1\nline2\nline3_new\nline4", "  line1\n  line2\n- line3\n+ line3_new\n  line4");
    }

    [Fact]
    public void Multilines3()
    {
        Test("", "line1\nline2", "- \n+ line1\n+ line2");
    }

    [Fact]
    public void Multilines4()
    {
        Test("line1\nline2", "", "- line1\n- line2\n+ ");
    }
}
