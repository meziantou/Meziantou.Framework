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
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryParseHexa()
    {
        AssertTryFromHexa("00", new byte[] { 0x00 });
        AssertTryFromHexa("01", new byte[] { 0x01 });
        AssertTryFromHexa("02", new byte[] { 0x02 });
        AssertTryFromHexa("03", new byte[] { 0x03 });
        AssertTryFromHexa("04", new byte[] { 0x04 });
        AssertTryFromHexa("05", new byte[] { 0x05 });
        AssertTryFromHexa("06", new byte[] { 0x06 });
        AssertTryFromHexa("07", new byte[] { 0x07 });
        AssertTryFromHexa("08", new byte[] { 0x08 });
        AssertTryFromHexa("09", new byte[] { 0x09 });
        AssertTryFromHexa("0a", new byte[] { 0x0A });
        AssertTryFromHexa("0b", new byte[] { 0x0B });
        AssertTryFromHexa("0c", new byte[] { 0x0C });
        AssertTryFromHexa("0d", new byte[] { 0x0D });
        AssertTryFromHexa("0e", new byte[] { 0x0E });
        AssertTryFromHexa("0f", new byte[] { 0x0F });
        AssertTryFromHexa("0A", new byte[] { 0x0A });
        AssertTryFromHexa("0B", new byte[] { 0x0B });
        AssertTryFromHexa("0C", new byte[] { 0x0C });
        AssertTryFromHexa("0D", new byte[] { 0x0D });
        AssertTryFromHexa("0E", new byte[] { 0x0E });
        AssertTryFromHexa("0F", new byte[] { 0x0F });
        AssertTryFromHexa("4Faf65", new byte[] { 0x4F, 0xAF, 0x65 });

        static void AssertTryFromHexa(string str, byte[] expected)
        {
            HexaConverter.TryParseHexaString(str, out var buffer).Should().BeTrue();
            buffer.Should().Equal(expected);
        }
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ParseHexa_WithPrefix()
    {
        HexaConverter.ParseHexaString("0x00").Should().Equal(new byte[] { 0x00 });
        HexaConverter.ParseHexaString("0x01").Should().Equal(new byte[] { 0x01 });
        HexaConverter.ParseHexaString("0x02").Should().Equal(new byte[] { 0x02 });
        HexaConverter.ParseHexaString("0x03").Should().Equal(new byte[] { 0x03 });
        HexaConverter.ParseHexaString("0x04").Should().Equal(new byte[] { 0x04 });
        HexaConverter.ParseHexaString("0x05").Should().Equal(new byte[] { 0x05 });
        HexaConverter.ParseHexaString("0x06").Should().Equal(new byte[] { 0x06 });
        HexaConverter.ParseHexaString("0x07").Should().Equal(new byte[] { 0x07 });
        HexaConverter.ParseHexaString("0x08").Should().Equal(new byte[] { 0x08 });
        HexaConverter.ParseHexaString("0x09").Should().Equal(new byte[] { 0x09 });
        HexaConverter.ParseHexaString("0x0a").Should().Equal(new byte[] { 0x0A });
        HexaConverter.ParseHexaString("0x0b").Should().Equal(new byte[] { 0x0B });
        HexaConverter.ParseHexaString("0x0c").Should().Equal(new byte[] { 0x0C });
        HexaConverter.ParseHexaString("0x0d").Should().Equal(new byte[] { 0x0D });
        HexaConverter.ParseHexaString("0x0e").Should().Equal(new byte[] { 0x0E });
        HexaConverter.ParseHexaString("0x0f").Should().Equal(new byte[] { 0x0F });
        HexaConverter.ParseHexaString("0x0A").Should().Equal(new byte[] { 0x0A });
        HexaConverter.ParseHexaString("0x0B").Should().Equal(new byte[] { 0x0B });
        HexaConverter.ParseHexaString("0x0C").Should().Equal(new byte[] { 0x0C });
        HexaConverter.ParseHexaString("0x0D").Should().Equal(new byte[] { 0x0D });
        HexaConverter.ParseHexaString("0x0E").Should().Equal(new byte[] { 0x0E });
        HexaConverter.ParseHexaString("0x0F").Should().Equal(new byte[] { 0x0F });

        HexaConverter.ParseHexaString("0x4Faf65").Should().Equal(new byte[] { 0x4F, 0xAF, 0x65 });
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
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ToHexa_Span_UpperCase()
    {
        var options = HexaOptions.UpperCase;
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x00 }, options).Should().Be("00");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x01 }, options).Should().Be("01");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x02 }, options).Should().Be("02");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x03 }, options).Should().Be("03");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x04 }, options).Should().Be("04");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x05 }, options).Should().Be("05");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x06 }, options).Should().Be("06");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x07 }, options).Should().Be("07");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x08 }, options).Should().Be("08");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x09 }, options).Should().Be("09");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0A }, options).Should().Be("0A");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0B }, options).Should().Be("0B");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0C }, options).Should().Be("0C");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0D }, options).Should().Be("0D");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0E }, options).Should().Be("0E");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0F }, options).Should().Be("0F");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options).Should().Be("102F");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void ToHexa_Span_LowerCase()
    {
        var options = HexaOptions.LowerCase;
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x00 }, options).Should().Be("00");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x01 }, options).Should().Be("01");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x02 }, options).Should().Be("02");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x03 }, options).Should().Be("03");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x04 }, options).Should().Be("04");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x05 }, options).Should().Be("05");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x06 }, options).Should().Be("06");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x07 }, options).Should().Be("07");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x08 }, options).Should().Be("08");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x09 }, options).Should().Be("09");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0A }, options).Should().Be("0a");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0B }, options).Should().Be("0b");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0C }, options).Should().Be("0c");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0D }, options).Should().Be("0d");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0E }, options).Should().Be("0e");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0F }, options).Should().Be("0f");
        HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options).Should().Be("102f");
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryParseHexa_Span()
    {
        AssertTryFromHexa("00", new byte[] { 0x00 });
        AssertTryFromHexa("01", new byte[] { 0x01 });
        AssertTryFromHexa("02", new byte[] { 0x02 });
        AssertTryFromHexa("03", new byte[] { 0x03 });
        AssertTryFromHexa("04", new byte[] { 0x04 });
        AssertTryFromHexa("05", new byte[] { 0x05 });
        AssertTryFromHexa("06", new byte[] { 0x06 });
        AssertTryFromHexa("07", new byte[] { 0x07 });
        AssertTryFromHexa("08", new byte[] { 0x08 });
        AssertTryFromHexa("09", new byte[] { 0x09 });
        AssertTryFromHexa("0a", new byte[] { 0x0A });
        AssertTryFromHexa("0b", new byte[] { 0x0B });
        AssertTryFromHexa("0c", new byte[] { 0x0C });
        AssertTryFromHexa("0d", new byte[] { 0x0D });
        AssertTryFromHexa("0e", new byte[] { 0x0E });
        AssertTryFromHexa("0f", new byte[] { 0x0F });
        AssertTryFromHexa("0A", new byte[] { 0x0A });
        AssertTryFromHexa("0B", new byte[] { 0x0B });
        AssertTryFromHexa("0C", new byte[] { 0x0C });
        AssertTryFromHexa("0D", new byte[] { 0x0D });
        AssertTryFromHexa("0E", new byte[] { 0x0E });
        AssertTryFromHexa("0F", new byte[] { 0x0F });
        AssertTryFromHexa("4Faf65", new byte[] { 0x4F, 0xAF, 0x65 });

        static void AssertTryFromHexa(string str, byte[] expected)
        {
            Span<byte> buffer = new byte[expected.Length];
            HexaConverter.TryParseHexaString(str, buffer, out var writtenBytes).Should().BeTrue();
            buffer.ToArray().Should().Equal(expected);
            writtenBytes.Should().Be(buffer.Length);
        }
    }

    [Fact]
    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    public void TryParseHexa_Span_WithPrefix()
    {
        HexaConverter.ParseHexaString("0x00").Should().Equal(new byte[] { 0x00 });
        HexaConverter.ParseHexaString("0x01").Should().Equal(new byte[] { 0x01 });
        HexaConverter.ParseHexaString("0x02").Should().Equal(new byte[] { 0x02 });
        HexaConverter.ParseHexaString("0x03").Should().Equal(new byte[] { 0x03 });
        HexaConverter.ParseHexaString("0x04").Should().Equal(new byte[] { 0x04 });
        HexaConverter.ParseHexaString("0x05").Should().Equal(new byte[] { 0x05 });
        HexaConverter.ParseHexaString("0x06").Should().Equal(new byte[] { 0x06 });
        HexaConverter.ParseHexaString("0x07").Should().Equal(new byte[] { 0x07 });
        HexaConverter.ParseHexaString("0x08").Should().Equal(new byte[] { 0x08 });
        HexaConverter.ParseHexaString("0x09").Should().Equal(new byte[] { 0x09 });
        HexaConverter.ParseHexaString("0x0a").Should().Equal(new byte[] { 0x0A });
        HexaConverter.ParseHexaString("0x0b").Should().Equal(new byte[] { 0x0B });
        HexaConverter.ParseHexaString("0x0c").Should().Equal(new byte[] { 0x0C });
        HexaConverter.ParseHexaString("0x0d").Should().Equal(new byte[] { 0x0D });
        HexaConverter.ParseHexaString("0x0e").Should().Equal(new byte[] { 0x0E });
        HexaConverter.ParseHexaString("0x0f").Should().Equal(new byte[] { 0x0F });
        HexaConverter.ParseHexaString("0x0A").Should().Equal(new byte[] { 0x0A });
        HexaConverter.ParseHexaString("0x0B").Should().Equal(new byte[] { 0x0B });
        HexaConverter.ParseHexaString("0x0C").Should().Equal(new byte[] { 0x0C });
        HexaConverter.ParseHexaString("0x0D").Should().Equal(new byte[] { 0x0D });
        HexaConverter.ParseHexaString("0x0E").Should().Equal(new byte[] { 0x0E });
        HexaConverter.ParseHexaString("0x0F").Should().Equal(new byte[] { 0x0F });

        HexaConverter.ParseHexaString("0x4Faf65").Should().Equal(new byte[] { 0x4F, 0xAF, 0x65 });
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
