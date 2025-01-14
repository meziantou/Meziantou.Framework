using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CommandLineTests;

public class CommandLineParserTests
{
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
