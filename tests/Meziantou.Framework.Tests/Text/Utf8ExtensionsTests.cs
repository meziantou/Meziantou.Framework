using Xunit;

namespace Meziantou.Framework.Text.Tests;

public sealed class Utf8ExtensionsTests
{
    [Fact]
    public void EnumerateRunesFromUtf8Test()
    {
        ReadOnlySpan<byte> bytes = "😊<√"u8.ToArray();
        var runes = new List<Rune>();
        foreach (var rune in bytes.EnumerateRunesFromUtf8())
        {
            runes.Add(rune);
        }

        var expected = new[]
        {
            new Rune('\uD83D', '\uDE0A'),
            new Rune('\u003C'),
            new Rune('\u221A'),
        };
        Assert.Equal(expected, runes);
    }
}
