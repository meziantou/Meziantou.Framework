using System.Text.RegularExpressions;

namespace Meziantou.Framework.Tests;

public sealed partial class RegexExtensionsTests
{
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
