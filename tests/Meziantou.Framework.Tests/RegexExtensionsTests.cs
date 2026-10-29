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

    [GeneratedRegex("[0-9]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigitRegex();
}
