using System.Text.RegularExpressions;

namespace Meziantou.Framework.Tests;

public sealed partial class RegexExtensionsTests
{
    [Fact]
    public async Task Documentation_ReplaceAsync()
    {
        #region RegexExtensions_ReplaceAsync
        var regex = new Regex(@"\{(?<name>\w+)\}", RegexOptions.None, TimeSpan.FromSeconds(1));

        var result = await regex.ReplaceAsync("Hello {name}, you have {count} messages", async match =>
        {
            await Task.Yield(); // For instance, read the value from a database
            return match.Groups["name"].Value switch
            {
                "name" => "Ada",
                "count" => "3",
                _ => match.Value,
            };
        });

        Assert.Equal("Hello Ada, you have 3 messages", result);
        #endregion
    }

    [Fact]
    public async Task ReplaceAsync()
    {
        var regex = DigitRegex();
        var actual = await regex.ReplaceAsync("a123b", async match =>
        {
            await Task.Yield();
            return "_";
        });
        Assert.Equal("a___b", actual);
    }

    [Theory]
    [InlineData("b", "abcb")]
    [InlineData("[0-9]+", "a1b22c333")]
    [InlineData("x", "abc")]
    [InlineData("^", "abc")]
    [InlineData("", "abc")]
    public async Task ReplaceAsync_RightToLeft(string pattern, string input)
    {
        var regex = new Regex(pattern, RegexOptions.RightToLeft, TimeSpan.FromSeconds(1));

        var expectedOrder = new List<int>();
        var expected = regex.Replace(input, match =>
        {
            expectedOrder.Add(match.Index);
            return "[" + match.Value.ToUpperInvariant() + "]";
        });

        var actualOrder = new List<int>();
        var actual = await regex.ReplaceAsync(input, async match =>
        {
            await Task.Yield();
            actualOrder.Add(match.Index);
            return "[" + match.Value.ToUpperInvariant() + "]";
        });

        Assert.Equal(expected, actual);
        Assert.Equal(expectedOrder, actualOrder);
    }

    [Fact]
    public async Task ReplaceAsync_RightToLeft_Simple()
    {
        var regex = new Regex("b", RegexOptions.RightToLeft, TimeSpan.FromSeconds(1));
        var actual = await regex.ReplaceAsync("abcb", match => Task.FromResult("x"));
        Assert.Equal("axcx", actual);
    }

    [Fact]
    public async Task ReplaceAsync_NullArguments()
    {
        var regex = DigitRegex();
        await Assert.ThrowsAsync<ArgumentNullException>("regex", () => RegexExtensions.ReplaceAsync(regex: null!, "a", match => Task.FromResult("x")));
        await Assert.ThrowsAsync<ArgumentNullException>("input", () => regex.ReplaceAsync(input: null!, match => Task.FromResult("x")));
        await Assert.ThrowsAsync<ArgumentNullException>("replacementFn", () => regex.ReplaceAsync("a", replacementFn: null!));
    }

    [GeneratedRegex("[0-9]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitRegex();
}
