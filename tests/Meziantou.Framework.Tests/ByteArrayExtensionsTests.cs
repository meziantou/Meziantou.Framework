#pragma warning disable MEZ_NET9
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ByteArrayExtensionsTests
{
    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ToHexa_UpperCase()
    {
        static void Test(byte[] value, string expectedValue)
        {
            Assert.Equal(expectedValue, HexaConverter.ToHexaString(value, HexaOptions.UpperCase));
            Assert.Equal(expectedValue, HexaConverter.ToHexaString((ReadOnlySpan<byte>)value, HexaOptions.UpperCase));
        }

        Test([0x00], "00");
        Test([0x01], "01");
        Test([0x02], "02");
        Test([0x03], "03");
        Test([0x04], "04");
        Test([0x05], "05");
        Test([0x06], "06");
        Test([0x07], "07");
        Test([0x08], "08");
        Test([0x09], "09");
        Test([0x0A], "0A");
        Test([0x0B], "0B");
        Test([0x0C], "0C");
        Test([0x0D], "0D");
        Test([0x0E], "0E");
        Test([0x0F], "0F");
        Test([0x10, 0x2F], "102F");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ToHexa_LowerCase()
    {
        static void Test(byte[] value, string expectedValue)
        {
            Assert.Equal(expectedValue, HexaConverter.ToHexaString(value, HexaOptions.LowerCase));
            Assert.Equal(expectedValue, HexaConverter.ToHexaString((ReadOnlySpan<byte>)value, HexaOptions.LowerCase));
        }

        Test([0x00], "00");
        Test([0x01], "01");
        Test([0x02], "02");
        Test([0x03], "03");
        Test([0x04], "04");
        Test([0x05], "05");
        Test([0x06], "06");
        Test([0x07], "07");
        Test([0x08], "08");
        Test([0x09], "09");
        Test([0x0A], "0a");
        Test([0x0B], "0b");
        Test([0x0C], "0c");
        Test([0x0D], "0d");
        Test([0x0E], "0e");
        Test([0x0F], "0f");
        Test([0x10, 0x2F], "102f");
    }

    [Fact]
    public void TryParseHexa()
    {
        AssertTryFromHexa("00", [0x00]);
        AssertTryFromHexa("01", [0x01]);
        AssertTryFromHexa("02", [0x02]);
        AssertTryFromHexa("03", [0x03]);
        AssertTryFromHexa("04", [0x04]);
        AssertTryFromHexa("05", [0x05]);
        AssertTryFromHexa("06", [0x06]);
        AssertTryFromHexa("07", [0x07]);
        AssertTryFromHexa("08", [0x08]);
        AssertTryFromHexa("09", [0x09]);
        AssertTryFromHexa("0a", [0x0A]);
        AssertTryFromHexa("0b", [0x0B]);
        AssertTryFromHexa("0c", [0x0C]);
        AssertTryFromHexa("0d", [0x0D]);
        AssertTryFromHexa("0e", [0x0E]);
        AssertTryFromHexa("0f", [0x0F]);
        AssertTryFromHexa("0A", [0x0A]);
        AssertTryFromHexa("0B", [0x0B]);
        AssertTryFromHexa("0C", [0x0C]);
        AssertTryFromHexa("0D", [0x0D]);
        AssertTryFromHexa("0E", [0x0E]);
        AssertTryFromHexa("0F", [0x0F]);
        AssertTryFromHexa("4Faf65", [0x4F, 0xAF, 0x65]);

        static void AssertTryFromHexa(string str, byte[] expected)
        {
            Assert.True(HexaConverter.TryParseHexaString(str, out var buffer));
            Assert.Equal(expected, buffer);
        }
    }

    [Fact]
    public void ParseHexa_WithPrefix()
    {
        Assert.Equal([0x00], HexaConverter.ParseHexaString("0x00"));
        Assert.Equal([0x01], HexaConverter.ParseHexaString("0x01"));
        Assert.Equal([0x02], HexaConverter.ParseHexaString("0x02"));
        Assert.Equal([0x03], HexaConverter.ParseHexaString("0x03"));
        Assert.Equal([0x04], HexaConverter.ParseHexaString("0x04"));
        Assert.Equal([0x05], HexaConverter.ParseHexaString("0x05"));
        Assert.Equal([0x06], HexaConverter.ParseHexaString("0x06"));
        Assert.Equal([0x07], HexaConverter.ParseHexaString("0x07"));
        Assert.Equal([0x08], HexaConverter.ParseHexaString("0x08"));
        Assert.Equal([0x09], HexaConverter.ParseHexaString("0x09"));
        Assert.Equal([0x0A], HexaConverter.ParseHexaString("0x0a"));
        Assert.Equal([0x0B], HexaConverter.ParseHexaString("0x0b"));
        Assert.Equal([0x0C], HexaConverter.ParseHexaString("0x0c"));
        Assert.Equal([0x0D], HexaConverter.ParseHexaString("0x0d"));
        Assert.Equal([0x0E], HexaConverter.ParseHexaString("0x0e"));
        Assert.Equal([0x0F], HexaConverter.ParseHexaString("0x0f"));
        Assert.Equal([0x0A], HexaConverter.ParseHexaString("0x0A"));
        Assert.Equal([0x0B], HexaConverter.ParseHexaString("0x0B"));
        Assert.Equal([0x0C], HexaConverter.ParseHexaString("0x0C"));
        Assert.Equal([0x0D], HexaConverter.ParseHexaString("0x0D"));
        Assert.Equal([0x0E], HexaConverter.ParseHexaString("0x0E"));
        Assert.Equal([0x0F], HexaConverter.ParseHexaString("0x0F"));
        Assert.Equal([0x4F, 0xAF, 0x65], HexaConverter.ParseHexaString("0x4Faf65"));
    }

    [Fact]
    public void ParseHexa_InvalidCharacters()
    {
        new Func<object>(() => HexaConverter.ParseHexaString("0H")).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void ParseHexa_InvalidLength()
    {
        new Func<object>(() => HexaConverter.ParseHexaString("000")).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void ToHexa_Span_UpperCase()
    {
        var options = HexaOptions.UpperCase;
        Assert.Equal("00", HexaConverter.ToHexaString([0x00], options));
        Assert.Equal("01", HexaConverter.ToHexaString([0x01], options));
        Assert.Equal("02", HexaConverter.ToHexaString([0x02], options));
        Assert.Equal("03", HexaConverter.ToHexaString([0x03], options));
        Assert.Equal("04", HexaConverter.ToHexaString([0x04], options));
        Assert.Equal("05", HexaConverter.ToHexaString([0x05], options));
        Assert.Equal("06", HexaConverter.ToHexaString([0x06], options));
        Assert.Equal("07", HexaConverter.ToHexaString([0x07], options));
        Assert.Equal("08", HexaConverter.ToHexaString([0x08], options));
        Assert.Equal("09", HexaConverter.ToHexaString([0x09], options));
        Assert.Equal("0A", HexaConverter.ToHexaString([0x0A], options));
        Assert.Equal("0B", HexaConverter.ToHexaString([0x0B], options));
        Assert.Equal("0C", HexaConverter.ToHexaString([0x0C], options));
        Assert.Equal("0D", HexaConverter.ToHexaString([0x0D], options));
        Assert.Equal("0E", HexaConverter.ToHexaString([0x0E], options));
        Assert.Equal("0F", HexaConverter.ToHexaString([0x0F], options));
        Assert.Equal("102F", HexaConverter.ToHexaString([0x10, 0x2F], options));
    }

    [Fact]
    public void ToHexa_Span_LowerCase()
    {
        var options = HexaOptions.LowerCase;
        Assert.Equal("00", HexaConverter.ToHexaString([0x00], options));
        Assert.Equal("01", HexaConverter.ToHexaString([0x01], options));
        Assert.Equal("02", HexaConverter.ToHexaString([0x02], options));
        Assert.Equal("03", HexaConverter.ToHexaString([0x03], options));
        Assert.Equal("04", HexaConverter.ToHexaString([0x04], options));
        Assert.Equal("05", HexaConverter.ToHexaString([0x05], options));
        Assert.Equal("06", HexaConverter.ToHexaString([0x06], options));
        Assert.Equal("07", HexaConverter.ToHexaString([0x07], options));
        Assert.Equal("08", HexaConverter.ToHexaString([0x08], options));
        Assert.Equal("09", HexaConverter.ToHexaString([0x09], options));
        Assert.Equal("0a", HexaConverter.ToHexaString([0x0A], options));
        Assert.Equal("0b", HexaConverter.ToHexaString([0x0B], options));
        Assert.Equal("0c", HexaConverter.ToHexaString([0x0C], options));
        Assert.Equal("0d", HexaConverter.ToHexaString([0x0D], options));
        Assert.Equal("0e", HexaConverter.ToHexaString([0x0E], options));
        Assert.Equal("0f", HexaConverter.ToHexaString([0x0F], options));
        Assert.Equal("102f", HexaConverter.ToHexaString([0x10, 0x2F], options));
    }

    [Fact]
    public void TryParseHexa_Span()
    {
        AssertTryFromHexa("00", [0x00]);
        AssertTryFromHexa("01", [0x01]);
        AssertTryFromHexa("02", [0x02]);
        AssertTryFromHexa("03", [0x03]);
        AssertTryFromHexa("04", [0x04]);
        AssertTryFromHexa("05", [0x05]);
        AssertTryFromHexa("06", [0x06]);
        AssertTryFromHexa("07", [0x07]);
        AssertTryFromHexa("08", [0x08]);
        AssertTryFromHexa("09", [0x09]);
        AssertTryFromHexa("0a", [0x0A]);
        AssertTryFromHexa("0b", [0x0B]);
        AssertTryFromHexa("0c", [0x0C]);
        AssertTryFromHexa("0d", [0x0D]);
        AssertTryFromHexa("0e", [0x0E]);
        AssertTryFromHexa("0f", [0x0F]);
        AssertTryFromHexa("0A", [0x0A]);
        AssertTryFromHexa("0B", [0x0B]);
        AssertTryFromHexa("0C", [0x0C]);
        AssertTryFromHexa("0D", [0x0D]);
        AssertTryFromHexa("0E", [0x0E]);
        AssertTryFromHexa("0F", [0x0F]);
        AssertTryFromHexa("4Faf65", [0x4F, 0xAF, 0x65]);

        static void AssertTryFromHexa(string str, byte[] expected)
        {
            Span<byte> buffer = new byte[expected.Length];
            Assert.True(HexaConverter.TryParseHexaString(str, buffer, out var writtenBytes));
            Assert.Equal(expected, buffer.ToArray());
            Assert.Equal(buffer.Length, writtenBytes);
        }
    }

    [Fact]
    public void TryParseHexa_Span_WithPrefix()
    {
        Assert.Equal([0x00], HexaConverter.ParseHexaString("0x00"));
        Assert.Equal([0x01], HexaConverter.ParseHexaString("0x01"));
        Assert.Equal([0x02], HexaConverter.ParseHexaString("0x02"));
        Assert.Equal([0x03], HexaConverter.ParseHexaString("0x03"));
        Assert.Equal([0x04], HexaConverter.ParseHexaString("0x04"));
        Assert.Equal([0x05], HexaConverter.ParseHexaString("0x05"));
        Assert.Equal([0x06], HexaConverter.ParseHexaString("0x06"));
        Assert.Equal([0x07], HexaConverter.ParseHexaString("0x07"));
        Assert.Equal([0x08], HexaConverter.ParseHexaString("0x08"));
        Assert.Equal([0x09], HexaConverter.ParseHexaString("0x09"));
        Assert.Equal([0x0A], HexaConverter.ParseHexaString("0x0a"));
        Assert.Equal([0x0B], HexaConverter.ParseHexaString("0x0b"));
        Assert.Equal([0x0C], HexaConverter.ParseHexaString("0x0c"));
        Assert.Equal([0x0D], HexaConverter.ParseHexaString("0x0d"));
        Assert.Equal([0x0E], HexaConverter.ParseHexaString("0x0e"));
        Assert.Equal([0x0F], HexaConverter.ParseHexaString("0x0f"));
        Assert.Equal([0x0A], HexaConverter.ParseHexaString("0x0A"));
        Assert.Equal([0x0B], HexaConverter.ParseHexaString("0x0B"));
        Assert.Equal([0x0C], HexaConverter.ParseHexaString("0x0C"));
        Assert.Equal([0x0D], HexaConverter.ParseHexaString("0x0D"));
        Assert.Equal([0x0E], HexaConverter.ParseHexaString("0x0E"));
        Assert.Equal([0x0F], HexaConverter.ParseHexaString("0x0F"));
        Assert.Equal([0x4F, 0xAF, 0x65], HexaConverter.ParseHexaString("0x4Faf65"));
    }

    [Fact]
    public void TryParseHexa_Span_InvalidCharacters()
    {
        Span<byte> bytes = new byte[10];
        Assert.False(HexaConverter.TryParseHexaString("0H", bytes, out var writtenBytes));
        Assert.Equal(0, writtenBytes);
    }

    [Fact]
    public void TryHexa_Span_BufferTooSmall()
    {
        Span<byte> bytes = new byte[10];
        Assert.False(HexaConverter.TryParseHexaString("000", bytes, out var writtenBytes));
        Assert.Equal(0, writtenBytes);
    }

    [Fact]
    public void TryHexa_Span_InvalidLength()
    {
        new Action(() =>
        {
            Span<byte> bytes = new byte[10];
            HexaConverter.ParseHexaString("000");
        }).Should().ThrowExactly<ArgumentException>();
    }
}
