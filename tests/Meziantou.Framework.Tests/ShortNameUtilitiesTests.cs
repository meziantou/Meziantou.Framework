using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ShortNameUtilitiesTests
{
    [Fact]
    public void CreateShortName_01()
    {
        // Arrange
        var name = "bbb";
        var names = new List<string> { "aaa", "aab" };

        // Act
        var shortName = ShortName.Create(names, 3, name);

        // Assert
        shortName.Should().Be("bbb");
    }

    [Fact]
    public void CreateShortName_02()
    {
        // Arrange
        var name = "aaa";
        var names = new List<string> { "aaa", "aab" };

        // Act
        var shortName = ShortName.Create(names, 3, name);

        // Assert
        shortName.Should().Be("aa0");
    }

    [Fact]
    public void BuildShortNames_01()
    {
        // Arrange
        var names = new List<string> { "aaaa", "aaab", "aaa", "aab", "other" };

        // Act
        var shortNames = ShortName.Create(names, 3, StringComparer.Ordinal);

        // Assert
        shortNames["aaaa"].Should().Be("aa0");
        shortNames["aaab"].Should().Be("aa1");
        shortNames["aaa"].Should().Be("aaa");
        shortNames["aab"].Should().Be("aab");
        shortNames["other"].Should().Be("oth");
    }
}
