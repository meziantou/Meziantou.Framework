using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public static class DictionaryExtensionsTests
{
    [Fact]
    public static void GetValue_KeyExists()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", 42 },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("test", "");

        // Assert
        actual.Should().Be("42");
    }

    [Fact]
    public static void GetValue_KeyNotExists()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", 42 },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("unknown", "");

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public static void GetValue_KeyNotConvertible()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", "aaa" },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("test", 0);

        // Assert
        actual.Should().Be(0);
    }
}
