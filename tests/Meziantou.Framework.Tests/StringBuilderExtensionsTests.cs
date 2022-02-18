using System.Text;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class StringBuilderExtensionsTests
{
    [Fact]
    public void AppendInvariant_FormattableString()
    {
        CultureInfoUtilities.UseCulture("sv-SE", () =>
        {
            var actual = new StringBuilder().AppendInvariant($"test{-42}").ToString();
            actual.Should().Be("test-42");
        });

        CultureInfoUtilities.UseCulture("en-US", () =>
        {
            var actual = new StringBuilder().AppendInvariant($"test{-42}").ToString();
            actual.Should().Be("test-42");
        });
    }

    [Theory]
    [InlineData("", 'a', false)]
    [InlineData("abc", 'c', true)]
    [InlineData("abc", 'd', false)]
    public void EndsWith_Test(string str, char c, bool expected)
    {
        var actual = new StringBuilder(str).EndsWith(c);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("", 'a', false)]
    [InlineData("abc", 'a', true)]
    [InlineData("abc", 'c', false)]
    public void StartsWith_Test(string str, char c, bool expected)
    {
        var actual = new StringBuilder(str).StartsWith(c);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'a', "bc")]
    [InlineData("aaabc", 'a', "bc")]
    [InlineData("abc", 'b', "abc")]
    public void TrimStart(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.TrimStart(c);
        actual.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'c', "ab")]
    [InlineData("abccc", 'c', "ab")]
    [InlineData("abc", 'b', "abc")]
    public void TrimEnd(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.TrimEnd(c);
        actual.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'c', "ab")]
    [InlineData("cccabccc", 'c', "ab")]
    [InlineData("cccacbccc", 'c', "acb")]
    [InlineData("abc", 'b', "abc")]
    public void Trim(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.Trim(c);
        actual.ToString().Should().Be(expected);
    }
}
