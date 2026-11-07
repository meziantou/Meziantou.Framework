namespace Meziantou.Framework.CommandLineTests;

public class CommandLineParserTests
{
    [Fact]
    public void ReadmeExample_Usage()
    {
        #region CommandLineParser_Usage
        // Parse current process arguments
        var currentParser = CommandLineParser.Current;
        Assert.NotNull(currentParser);

        // Or parse custom arguments
        var parser = new CommandLineParser();
        parser.Parse(["/name=John", "/verbose", "input.txt"]);

        // Check if an argument exists
        Assert.True(parser.HasArgument("verbose"));

        // Get named argument value
        Assert.Equal("John", parser.GetArgument("name"));

        // Get positional argument (the index in the array of arguments)
        Assert.Equal("input.txt", parser.GetArgument(2));

        // Check if help was requested: detects -?, /?, -help, /help, --help
        Assert.False(parser.HelpRequested);
        #endregion
    }

    [Fact]
    public void CommandLineParserExample()
    {
        #region CommandLineParser_Example
        // CommandLineParser.Current contains the arguments of the current process.
        // You can also parse custom arguments:
        var parser = new CommandLineParser();
        parser.Parse(["/name=John", "/verbose", "input.txt", "--help"]);

        // Check if an argument exists
        Assert.True(parser.HasArgument("verbose"));

        // Get named argument value
        Assert.Equal("John", parser.GetArgument("name"));

        // Get argument at position (zero-based index in the argument array)
        Assert.Equal("input.txt", parser.GetArgument(2));

        // Check if help was requested (detects -?, /?, -help, /help, --help)
        Assert.True(parser.HelpRequested);
        #endregion
    }

    [Fact]
    public void CommandLineParser_ArgumentFormats()
    {
        #region CommandLineParser_ArgumentFormats
        var parser = new CommandLineParser();
        parser.Parse(["-output:result.txt", "/Level=3", "/force", "input.txt"]);

        // Names are case-insensitive, and the value follows the first ':' or '='
        Assert.Equal("result.txt", parser.GetArgument("OUTPUT"));
        Assert.Equal("3", parser.GetArgument("level"));

        // A named argument without a value has an empty value
        Assert.True(parser.HasArgument("force"));
        Assert.Equal("", parser.GetArgument("force"));

        // Missing arguments return null
        Assert.Null(parser.GetArgument("verbose"));
        Assert.Null(parser.GetArgument(10));

        // Every argument keeps its position, named arguments included (without their prefix)
        Assert.Equal("input.txt", parser.GetArgument(3));
        Assert.Equal("force", parser.GetArgument(2));
        #endregion
    }

    [Fact]
    public void CommandLineParser_HelpArguments()
    {
        #region CommandLineParser_HelpArguments
        var parser = new CommandLineParser();
        parser.Parse(["--help"]);

        // Help arguments set HelpRequested, and are not named or positional arguments
        Assert.True(parser.HelpRequested);
        Assert.False(parser.HasArgument("help"));
        Assert.Null(parser.GetArgument(0));
        #endregion
    }

    [Fact]
    public void CommandLineParser_Prefixes()
    {
        #region CommandLineParser_Prefixes
        var parser = new CommandLineParser();
        parser.Parse(["--verbose", "/tmp/file.txt"]);

        // The prefix is a single '-' or '/': the second '-' is part of the name
        Assert.False(parser.HasArgument("verbose"));
        Assert.True(parser.HasArgument("-verbose"));

        // A Unix absolute path starts with '/', so it is read as a named argument, and loses its '/' at its position too
        Assert.True(parser.HasArgument("tmp/file.txt"));
        Assert.Equal("tmp/file.txt", parser.GetArgument(1));
        #endregion
    }

    [Fact]
    public void HasArgument_01()
    {
        // Arrange
        var args = new[] { "/a", "/b=value2" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var valueA = parser.HasArgument("a");
        var valueB = parser.HasArgument("b");
        var valueC = parser.HasArgument("c");
        Assert.True(valueA);
        Assert.True(valueB);
        Assert.False(valueC);
    }

    [Fact]
    public void GetArgument_01()
    {
        // Arrange
        var args = new[] { "/a=value1", "/b=value2" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var valueA = parser.GetArgument("a");
        var valueB = parser.GetArgument("b");
        var helpRequested = parser.HelpRequested;
        Assert.Equal("value1", valueA);
        Assert.Equal("value2", valueB);
        Assert.False(helpRequested);
    }

    [Fact]
    public void GetArgument_02()
    {
        // Arrange
        var args = new[] { "/a=value1", "value2" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var valueA = parser.GetArgument("a");
        var valueB = parser.GetArgument(1);
        var helpRequested = parser.HelpRequested;
        Assert.Equal("value1", valueA);
        Assert.Equal("value2", valueB);
        Assert.False(helpRequested);
    }

    [Fact]
    public void GetArgument_TrailingWhitespace()
    {
        // Arrange
        var args = new[] { "/a=value1 ", "value2" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var valueA = parser.GetArgument("a");
        var valueB = parser.GetArgument(1);
        var helpRequested = parser.HelpRequested;
        Assert.Equal("value1 ", valueA);
        Assert.Equal("value2", valueB);
        Assert.False(helpRequested);
    }

    [Fact]
    public void GetArgument_OnlyWhitespace()
    {
        // Arrange
        var args = new[] { "   " };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var valueA = parser.GetArgument(0);
        var helpRequested = parser.HelpRequested;
        Assert.Equal("   ", valueA);
        Assert.False(helpRequested);
    }

    [Fact]
    public void Current_ShouldNotIncludeTheExecutablePath()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        var expected = new CommandLineParser();
        expected.Parse(commandLineArgs[1..]);

        // Position 0 must be the first argument of the process, not the path of the executable.
        for (var i = 0; i < commandLineArgs.Length; i++)
        {
            Assert.Equal(expected.GetArgument(i), CommandLineParser.Current.GetArgument(i));
        }
    }

    [Fact]
    public void HelpRequested_01()
    {
        // Arrange
        var args = new[] { "/?" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var helpRequested = parser.HelpRequested;
        Assert.True(helpRequested);
    }

    [Fact]
    public void HelpRequested_02()
    {
        // Arrange
        var args = new[] { "test", "/help" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var helpRequested = parser.HelpRequested;
        Assert.True(helpRequested);
    }

    [Fact]
    public void HelpRequested_03()
    {
        // Arrange
        var args = new[] { "test", "test" };
        var parser = new CommandLineParser();
        parser.Parse(args);

        // Act
        var helpRequested = parser.HelpRequested;
        Assert.False(helpRequested);
    }
}
