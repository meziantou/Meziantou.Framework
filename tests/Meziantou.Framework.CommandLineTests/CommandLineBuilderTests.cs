using TestUtilities;

namespace Meziantou.Framework.CommandLineTests;

public class CommandLineBuilderTests(ArgumentPrinterClassFixture argumentPrinterFixture) : IClassFixture<ArgumentPrinterClassFixture>
{
    public static TheoryData<string, string> GetArguments()
    {
        var result = new TheoryData<string, string>();
        Add(@"a");
        Add(@"arg 1");
        Add(@"\some\path with\spaces");
        Add(@"a\\b");
        Add(@"a\\\\b");
        Add(@"""a");
        Add(@"a|b");
        Add(@"ab|");
        Add(@"|ab");
        Add(@"^ab");
        Add(@"a^b");
        Add(@"ab^");
        Add(@"malicious argument"" & whoami");
        Add(@"""malicious-argument\^""^&whoami""");
        return result;

        void Add(string value) => result.Add(value, value);
    }

    [Theory]
    [MemberData(nameof(GetArguments))]
    public async Task WindowsQuotedArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsQuotedArgument(value);
        var actualArguments = await argumentPrinterFixture.RoundtripArguments(args);
        Assert.Equal([expected], actualArguments);
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [MemberData(nameof(GetArguments))]
    public async Task WindowsCmdArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsCmdArgument(value);
        var actualArguments = await argumentPrinterFixture.RoundtripCmdArguments(args);
        Assert.Equal([expected], actualArguments);
    }
}
