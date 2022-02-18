using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CommandLineTests;

[Collection("PromptTests")]
public class PromptTests
{
    [Fact]
    public void YesNo_ShouldUseDefaultValue1()
    {
        UsingConsole("\r\n", () =>
        {
            var result = Prompt.YesNo("test?", defaultValue: true);
            result.Should().BeTrue();
        });
    }

    [Fact]
    public void YesNo_ShouldUseDefaultValue2()
    {
        UsingConsole("\r\n", () =>
        {
            var result = Prompt.YesNo("test?", defaultValue: false);
            result.Should().BeFalse();
        });
    }

    [Fact]
    public void YesNo_ShouldUseParseYesValue()
    {
        UsingConsole("Y\r\n", () =>
        {
            var result = Prompt.YesNo("test?", defaultValue: null);
            result.Should().BeTrue();
        });
    }

    [Fact]
    public void YesNo_ShouldUseParseNoValue()
    {
        UsingConsole("no\r\n", () =>
        {
            var result = Prompt.YesNo("test?", "Yes", "No", defaultValue: null);
            result.Should().BeFalse();
        });
    }

    [Fact]
    public void YesNo_ShouldAskAgainWhenValueIsInvalid()
    {
        var output = UsingConsole("test\r\nYes\r\n", () =>
        {
            var result = Prompt.YesNo("test?", "Yes", "No", defaultValue: null);
            result.Should().BeTrue();
        });

        output.IndexOf("test?", StringComparison.Ordinal).Should().Be(0);
        output.LastIndexOf("test?", StringComparison.Ordinal).Should().BePositive();
    }

    private static string UsingConsole(string input, Action action)
    {
        var initialInStream = Console.In;
        var initialOutStream = Console.Out;
        try
        {
            using var inStream = new StringReader(input);
            using var outStream = new StringWriter();
            Console.SetIn(inStream);
            Console.SetOut(outStream);

            action();
            return outStream.ToString();
        }
        finally
        {
            Console.SetIn(initialInStream);
            Console.SetOut(initialOutStream);
        }
    }
}
