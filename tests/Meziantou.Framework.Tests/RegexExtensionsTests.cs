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

    [Fact]
    public async Task Documentation_ReplaceAsync_CountAndCancellation()
    {
        #region RegexExtensions_ReplaceAsync_Count
        var regex = new Regex(@"[0-9]+", RegexOptions.None, TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Replace the first 2 matches, searching from the 2nd character. The callback receives the token.
        var result = await regex.ReplaceAsync("1 22 333 4444", async (match, cancellationToken) =>
        {
            await Task.Delay(1, cancellationToken); // For instance, call a service
            return $"<{match.Value}>";
        }, count: 2, startat: 1, cts.Token);

        Assert.Equal("1 <22> <333> 4444", result);
        #endregion
    }

    [Theory]
    [InlineData("[0-9]+", RegexOptions.None, "a1b22c333d", -1, 0)]
    [InlineData("[0-9]+", RegexOptions.None, "a1b22c333d", 0, 0)]
    [InlineData("[0-9]+", RegexOptions.None, "a1b22c333d", 1, 0)]
    [InlineData("[0-9]+", RegexOptions.None, "a1b22c333d", 2, 3)]
    [InlineData("[0-9]+", RegexOptions.None, "a1b22c333d", -1, 10)]
    [InlineData("[0-9]+", RegexOptions.RightToLeft, "a1b22c333d", -1, 10)]
    [InlineData("[0-9]+", RegexOptions.RightToLeft, "a1b22c333d", 2, 10)]
    [InlineData("[0-9]+", RegexOptions.RightToLeft, "a1b22c333d", 1, 5)]
    [InlineData("[0-9]+", RegexOptions.RightToLeft, "a1b22c333d", -1, 0)]
    [InlineData("", RegexOptions.None, "abc", 2, 1)]
    [InlineData("^", RegexOptions.None, "abc", -1, 1)]
    public async Task ReplaceAsync_CountAndStartAt_MatchesRegexReplace(string pattern, RegexOptions options, string input, int count, int startat)
    {
        var regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
        var expected = regex.Replace(input, match => "[" + match.Value + "]", count, startat);

        var actualTask = await regex.ReplaceAsync(input, match => Task.FromResult("[" + match.Value + "]"), count, startat);
        var actualValueTask = await regex.ReplaceAsync(input, (match, _) => ValueTask.FromResult("[" + match.Value + "]"), count, startat);

        Assert.Equal(expected, actualTask);
        Assert.Equal(expected, actualValueTask);
        if (startat == (regex.RightToLeft ? input.Length : 0))
        {
            Assert.Equal(expected, await regex.ReplaceAsync(input, match => Task.FromResult("[" + match.Value + "]"), count));
            Assert.Equal(expected, await regex.ReplaceAsync(input, (match, _) => ValueTask.FromResult("[" + match.Value + "]"), count));
        }
    }

    [Fact]
    public async Task ReplaceAsync_Cancellation()
    {
        var regex = DigitRegex();
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Assert.ThrowsAny<OperationCanceledException>(async () =>
        {
            await regex.ReplaceAsync("123", match =>
            {
                calls++;
                cts.Cancel();
                return Task.FromResult("x");
            }, cts.Token);
        });

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReplaceAsync_InvalidCountOrStartAt()
    {
        var regex = DigitRegex();
        await Assert.Throws<ArgumentOutOfRangeException>("count", async () => { await regex.ReplaceAsync("a", match => Task.FromResult("x"), count: -2); });
        await Assert.Throws<ArgumentOutOfRangeException>("startat", async () => { await regex.ReplaceAsync("a", match => Task.FromResult("x"), count: -1, startat: -1); });
        await Assert.Throws<ArgumentOutOfRangeException>("startat", async () => { await regex.ReplaceAsync("a", match => Task.FromResult("x"), count: -1, startat: 2); });
    }

    [GeneratedRegex("[0-9]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitRegex();
}
