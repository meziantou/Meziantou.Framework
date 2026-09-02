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

    [Fact]
    public void InvalidByteYieldsTheReplacementCharacterAndEnumerationContinues()
    {
        ReadOnlySpan<byte> bytes = [0x41, 0xFF, 0x42];

        var runes = new List<Rune>();
        foreach (var rune in bytes.EnumerateRunesFromUtf8())
        {
            runes.Add(rune);
        }

        Assert.Equal([new Rune('A'), Rune.ReplacementChar, new Rune('B')], runes);
    }

    [Fact]
    public void IncompleteTrailingSequenceYieldsTheReplacementCharacter()
    {
        // The leading byte of a 3-byte sequence with its continuation bytes missing
        ReadOnlySpan<byte> bytes = [0x41, 0xE2, 0x88];

        var runes = new List<Rune>();
        foreach (var rune in bytes.EnumerateRunesFromUtf8())
        {
            runes.Add(rune);
        }

        Assert.Equal([new Rune('A'), Rune.ReplacementChar], runes);
    }

    [Fact]
    public void EmptyInputYieldsNothing()
    {
        ReadOnlySpan<byte> bytes = [];

        var runes = new List<Rune>();
        foreach (var rune in bytes.EnumerateRunesFromUtf8())
        {
            runes.Add(rune);
        }

        Assert.Empty(runes);
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0xFF, 0x42 })]
    [InlineData(new byte[] { 0x41, 0xE2, 0x88 })]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x8A, 0x3C })]
    [InlineData(new byte[] { 0x80 })]
    public void MatchesMemoryExtensionsEnumerateRunes(byte[] bytes)
    {
        var actual = new List<Rune>();
        foreach (var rune in new ReadOnlySpan<byte>(bytes).EnumerateRunesFromUtf8())
        {
            actual.Add(rune);
        }

        var expected = new List<Rune>();
        foreach (var rune in Encoding.UTF8.GetString(bytes).EnumerateRunes())
        {
            expected.Add(rune);
        }

        Assert.Equal(expected, actual);
    }
}
