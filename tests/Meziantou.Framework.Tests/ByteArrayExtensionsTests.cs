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
        var options = HexaOptions.UpperCase;
        HexaConverter.ToHexaString(new byte[] { 0x00 }, options).Should().Be("00");
        HexaConverter.ToHexaString(new byte[] { 0x01 }, options).Should().Be("01");
        HexaConverter.ToHexaString(new byte[] { 0x02 }, options).Should().Be("02");
        HexaConverter.ToHexaString(new byte[] { 0x03 }, options).Should().Be("03");
        HexaConverter.ToHexaString(new byte[] { 0x04 }, options).Should().Be("04");
        HexaConverter.ToHexaString(new byte[] { 0x05 }, options).Should().Be("05");
        HexaConverter.ToHexaString(new byte[] { 0x06 }, options).Should().Be("06");
        HexaConverter.ToHexaString(new byte[] { 0x07 }, options).Should().Be("07");
        HexaConverter.ToHexaString(new byte[] { 0x08 }, options).Should().Be("08");
        HexaConverter.ToHexaString(new byte[] { 0x09 }, options).Should().Be("09");
        HexaConverter.ToHexaString(new byte[] { 0x0A }, options).Should().Be("0A");
        HexaConverter.ToHexaString(new byte[] { 0x0B }, options).Should().Be("0B");
        HexaConverter.ToHexaString(new byte[] { 0x0C }, options).Should().Be("0C");
        HexaConverter.ToHexaString(new byte[] { 0x0D }, options).Should().Be("0D");
        HexaConverter.ToHexaString(new byte[] { 0x0E }, options).Should().Be("0E");
        HexaConverter.ToHexaString(new byte[] { 0x0F }, options).Should().Be("0F");
        HexaConverter.ToHexaString(new byte[] { 0x10, 0x2F }, options).Should().Be("102F");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ToHexa_LowerCase()
    {
        var options = HexaOptions.LowerCase;
        HexaConverter.ToHexaString(new byte[] { 0x00 }, options).Should().Be("00");
        HexaConverter.ToHexaString(new byte[] { 0x01 }, options).Should().Be("01");
        HexaConverter.ToHexaString(new byte[] { 0x02 }, options).Should().Be("02");
        HexaConverter.ToHexaString(new byte[] { 0x03 }, options).Should().Be("03");
        HexaConverter.ToHexaString(new byte[] { 0x04 }, options).Should().Be("04");
        HexaConverter.ToHexaString(new byte[] { 0x05 }, options).Should().Be("05");
        HexaConverter.ToHexaString(new byte[] { 0x06 }, options).Should().Be("06");
        HexaConverter.ToHexaString(new byte[] { 0x07 }, options).Should().Be("07");
        HexaConverter.ToHexaString(new byte[] { 0x08 }, options).Should().Be("08");
        HexaConverter.ToHexaString(new byte[] { 0x09 }, options).Should().Be("09");
        HexaConverter.ToHexaString(new byte[] { 0x0A }, options).Should().Be("0a");
        HexaConverter.ToHexaString(new byte[] { 0x0B }, options).Should().Be("0b");
        HexaConverter.ToHexaString(new byte[] { 0x0C }, options).Should().Be("0c");
        HexaConverter.ToHexaString(new byte[] { 0x0D }, options).Should().Be("0d");
        HexaConverter.ToHexaString(new byte[] { 0x0E }, options).Should().Be("0e");
        HexaConverter.ToHexaString(new byte[] { 0x0F }, options).Should().Be("0f");
        HexaConverter.ToHexaString(new byte[] { 0x10, 0x2F }, options).Should().Be("102f");
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
            HexaConverter.TryParseHexaString(str, out var buffer).Should().BeTrue();
            buffer.Should().Equal(expected);
        }
    }

    [Fact]
    public void ParseHexa_WithPrefix()
    {
        HexaConverter.ParseHexaString("0x00").Should().Equal([0x00]);
        HexaConverter.ParseHexaString("0x01").Should().Equal([0x01]);
        HexaConverter.ParseHexaString("0x02").Should().Equal([0x02]);
        HexaConverter.ParseHexaString("0x03").Should().Equal([0x03]);
        HexaConverter.ParseHexaString("0x04").Should().Equal([0x04]);
        HexaConverter.ParseHexaString("0x05").Should().Equal([0x05]);
        HexaConverter.ParseHexaString("0x06").Should().Equal([0x06]);
        HexaConverter.ParseHexaString("0x07").Should().Equal([0x07]);
        HexaConverter.ParseHexaString("0x08").Should().Equal([0x08]);
        HexaConverter.ParseHexaString("0x09").Should().Equal([0x09]);
        HexaConverter.ParseHexaString("0x0a").Should().Equal([0x0A]);
        HexaConverter.ParseHexaString("0x0b").Should().Equal([0x0B]);
        HexaConverter.ParseHexaString("0x0c").Should().Equal([0x0C]);
        HexaConverter.ParseHexaString("0x0d").Should().Equal([0x0D]);
        HexaConverter.ParseHexaString("0x0e").Should().Equal([0x0E]);
        HexaConverter.ParseHexaString("0x0f").Should().Equal([0x0F]);
        HexaConverter.ParseHexaString("0x0A").Should().Equal([0x0A]);
        HexaConverter.ParseHexaString("0x0B").Should().Equal([0x0B]);
        HexaConverter.ParseHexaString("0x0C").Should().Equal([0x0C]);
        HexaConverter.ParseHexaString("0x0D").Should().Equal([0x0D]);
        HexaConverter.ParseHexaString("0x0E").Should().Equal([0x0E]);
        HexaConverter.ParseHexaString("0x0F").Should().Equal([0x0F]);

        HexaConverter.ParseHexaString("0x4Faf65").Should().Equal([0x4F, 0xAF, 0x65]);
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
        HexaConverter.ToHexaString([0x00], options).Should().Be("00");
        HexaConverter.ToHexaString([0x01], options).Should().Be("01");
        HexaConverter.ToHexaString([0x02], options).Should().Be("02");
        HexaConverter.ToHexaString([0x03], options).Should().Be("03");
        HexaConverter.ToHexaString([0x04], options).Should().Be("04");
        HexaConverter.ToHexaString([0x05], options).Should().Be("05");
        HexaConverter.ToHexaString([0x06], options).Should().Be("06");
        HexaConverter.ToHexaString([0x07], options).Should().Be("07");
        HexaConverter.ToHexaString([0x08], options).Should().Be("08");
        HexaConverter.ToHexaString([0x09], options).Should().Be("09");
        HexaConverter.ToHexaString([0x0A], options).Should().Be("0A");
        HexaConverter.ToHexaString([0x0B], options).Should().Be("0B");
        HexaConverter.ToHexaString([0x0C], options).Should().Be("0C");
        HexaConverter.ToHexaString([0x0D], options).Should().Be("0D");
        HexaConverter.ToHexaString([0x0E], options).Should().Be("0E");
        HexaConverter.ToHexaString([0x0F], options).Should().Be("0F");
        HexaConverter.ToHexaString([0x10, 0x2F], options).Should().Be("102F");
    }

    [Fact]
    public void ToHexa_Span_LowerCase()
    {
        var options = HexaOptions.LowerCase;
        HexaConverter.ToHexaString([0x00], options).Should().Be("00");
        HexaConverter.ToHexaString([0x01], options).Should().Be("01");
        HexaConverter.ToHexaString([0x02], options).Should().Be("02");
        HexaConverter.ToHexaString([0x03], options).Should().Be("03");
        HexaConverter.ToHexaString([0x04], options).Should().Be("04");
        HexaConverter.ToHexaString([0x05], options).Should().Be("05");
        HexaConverter.ToHexaString([0x06], options).Should().Be("06");
        HexaConverter.ToHexaString([0x07], options).Should().Be("07");
        HexaConverter.ToHexaString([0x08], options).Should().Be("08");
        HexaConverter.ToHexaString([0x09], options).Should().Be("09");
        HexaConverter.ToHexaString([0x0A], options).Should().Be("0a");
        HexaConverter.ToHexaString([0x0B], options).Should().Be("0b");
        HexaConverter.ToHexaString([0x0C], options).Should().Be("0c");
        HexaConverter.ToHexaString([0x0D], options).Should().Be("0d");
        HexaConverter.ToHexaString([0x0E], options).Should().Be("0e");
        HexaConverter.ToHexaString([0x0F], options).Should().Be("0f");
        HexaConverter.ToHexaString([0x10, 0x2F], options).Should().Be("102f");
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
            HexaConverter.TryParseHexaString(str, buffer, out var writtenBytes).Should().BeTrue();
            buffer.ToArray().Should().Equal(expected);
            writtenBytes.Should().Be(buffer.Length);
        }
    }

    [Fact]
    public void TryParseHexa_Span_WithPrefix()
    {
        HexaConverter.ParseHexaString("0x00").Should().Equal([0x00]);
        HexaConverter.ParseHexaString("0x01").Should().Equal([0x01]);
        HexaConverter.ParseHexaString("0x02").Should().Equal([0x02]);
        HexaConverter.ParseHexaString("0x03").Should().Equal([0x03]);
        HexaConverter.ParseHexaString("0x04").Should().Equal([0x04]);
        HexaConverter.ParseHexaString("0x05").Should().Equal([0x05]);
        HexaConverter.ParseHexaString("0x06").Should().Equal([0x06]);
        HexaConverter.ParseHexaString("0x07").Should().Equal([0x07]);
        HexaConverter.ParseHexaString("0x08").Should().Equal([0x08]);
        HexaConverter.ParseHexaString("0x09").Should().Equal([0x09]);
        HexaConverter.ParseHexaString("0x0a").Should().Equal([0x0A]);
        HexaConverter.ParseHexaString("0x0b").Should().Equal([0x0B]);
        HexaConverter.ParseHexaString("0x0c").Should().Equal([0x0C]);
        HexaConverter.ParseHexaString("0x0d").Should().Equal([0x0D]);
        HexaConverter.ParseHexaString("0x0e").Should().Equal([0x0E]);
        HexaConverter.ParseHexaString("0x0f").Should().Equal([0x0F]);
        HexaConverter.ParseHexaString("0x0A").Should().Equal([0x0A]);
        HexaConverter.ParseHexaString("0x0B").Should().Equal([0x0B]);
        HexaConverter.ParseHexaString("0x0C").Should().Equal([0x0C]);
        HexaConverter.ParseHexaString("0x0D").Should().Equal([0x0D]);
        HexaConverter.ParseHexaString("0x0E").Should().Equal([0x0E]);
        HexaConverter.ParseHexaString("0x0F").Should().Equal([0x0F]);

        HexaConverter.ParseHexaString("0x4Faf65").Should().Equal([0x4F, 0xAF, 0x65]);
    }

    [Fact]
    public void TryParseHexa_Span_InvalidCharacters()
    {
        Span<byte> bytes = new byte[10];
        HexaConverter.TryParseHexaString("0H", bytes, out var writtenBytes).Should().BeFalse();
        writtenBytes.Should().Be(0);
    }

    [Fact]
    public void TryHexa_Span_BufferTooSmall()
    {
        Span<byte> bytes = new byte[10];
        HexaConverter.TryParseHexaString("000", bytes, out var writtenBytes).Should().BeFalse();
        writtenBytes.Should().Be(0);
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
