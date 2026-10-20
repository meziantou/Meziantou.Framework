using Meziantou.Xunit;

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

    [Fact]
    public void ReadmeExample_Usage()
    {
        #region CommandLineBuilder_Usage
        // Quote a single argument for standard Windows applications
        var arg = CommandLineBuilder.WindowsQuotedArgument(@"path with spaces\file.txt");
        Assert.Equal(@"""path with spaces\file.txt""", arg);

        // Quote multiple arguments
        var args = CommandLineBuilder.WindowsQuotedArguments("arg1", "path with spaces", "normal");
        Assert.Equal(@"arg1 ""path with spaces"" normal", args);

        // Quote for cmd.exe (handles special characters like &, |, ^, etc.)
        var cmdArg = CommandLineBuilder.WindowsCmdArgument(@"malicious argument"" & whoami");
        Assert.Equal(@"^""malicious argument\^"" ^& whoami^""", cmdArg);

        var cmdArgs = CommandLineBuilder.WindowsCmdArguments("echo", "Hello & Goodbye");
        Assert.Equal(@"echo ^""Hello ^& Goodbye^""", cmdArgs);
        #endregion
    }

    [Theory]
    [MemberData(nameof(GetArguments))]
    public async Task WindowsQuotedArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsQuotedArgument(value);
        var actualArguments = await argumentPrinterFixture.RoundtripArguments(args);
        Assert.Equal([expected], actualArguments);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [MemberData(nameof(GetArguments))]
    public async Task WindowsCmdArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsCmdArgument(value);
        var actualArguments = await argumentPrinterFixture.RoundtripCmdArguments(args);
        Assert.Equal([expected], actualArguments);
    }
}
