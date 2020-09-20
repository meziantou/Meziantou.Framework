using System;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ByteArrayExtensionsTests
    {
        [Fact]
        public void ToHexa_UpperCase()
        {
            var options = HexaOptions.UpperCase;
            Assert.Equal("00", HexaConverter.ToHexaString(new byte[] { 0x00 }, options));
            Assert.Equal("01", HexaConverter.ToHexaString(new byte[] { 0x01 }, options));
            Assert.Equal("02", HexaConverter.ToHexaString(new byte[] { 0x02 }, options));
            Assert.Equal("03", HexaConverter.ToHexaString(new byte[] { 0x03 }, options));
            Assert.Equal("04", HexaConverter.ToHexaString(new byte[] { 0x04 }, options));
            Assert.Equal("05", HexaConverter.ToHexaString(new byte[] { 0x05 }, options));
            Assert.Equal("06", HexaConverter.ToHexaString(new byte[] { 0x06 }, options));
            Assert.Equal("07", HexaConverter.ToHexaString(new byte[] { 0x07 }, options));
            Assert.Equal("08", HexaConverter.ToHexaString(new byte[] { 0x08 }, options));
            Assert.Equal("09", HexaConverter.ToHexaString(new byte[] { 0x09 }, options));
            Assert.Equal("0A", HexaConverter.ToHexaString(new byte[] { 0x0A }, options));
            Assert.Equal("0B", HexaConverter.ToHexaString(new byte[] { 0x0B }, options));
            Assert.Equal("0C", HexaConverter.ToHexaString(new byte[] { 0x0C }, options));
            Assert.Equal("0D", HexaConverter.ToHexaString(new byte[] { 0x0D }, options));
            Assert.Equal("0E", HexaConverter.ToHexaString(new byte[] { 0x0E }, options));
            Assert.Equal("0F", HexaConverter.ToHexaString(new byte[] { 0x0F }, options));
            Assert.Equal("102F", HexaConverter.ToHexaString(new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
        public void ToHexa_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.Equal("00", HexaConverter.ToHexaString(new byte[] { 0x00 }, options));
            Assert.Equal("01", HexaConverter.ToHexaString(new byte[] { 0x01 }, options));
            Assert.Equal("02", HexaConverter.ToHexaString(new byte[] { 0x02 }, options));
            Assert.Equal("03", HexaConverter.ToHexaString(new byte[] { 0x03 }, options));
            Assert.Equal("04", HexaConverter.ToHexaString(new byte[] { 0x04 }, options));
            Assert.Equal("05", HexaConverter.ToHexaString(new byte[] { 0x05 }, options));
            Assert.Equal("06", HexaConverter.ToHexaString(new byte[] { 0x06 }, options));
            Assert.Equal("07", HexaConverter.ToHexaString(new byte[] { 0x07 }, options));
            Assert.Equal("08", HexaConverter.ToHexaString(new byte[] { 0x08 }, options));
            Assert.Equal("09", HexaConverter.ToHexaString(new byte[] { 0x09 }, options));
            Assert.Equal("0a", HexaConverter.ToHexaString(new byte[] { 0x0A }, options));
            Assert.Equal("0b", HexaConverter.ToHexaString(new byte[] { 0x0B }, options));
            Assert.Equal("0c", HexaConverter.ToHexaString(new byte[] { 0x0C }, options));
            Assert.Equal("0d", HexaConverter.ToHexaString(new byte[] { 0x0D }, options));
            Assert.Equal("0e", HexaConverter.ToHexaString(new byte[] { 0x0E }, options));
            Assert.Equal("0f", HexaConverter.ToHexaString(new byte[] { 0x0F }, options));
            Assert.Equal("102f", HexaConverter.ToHexaString(new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
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
                Assert.True(HexaConverter.TryParseHexaString(str, out var buffer));
                Assert.Equal(expected, buffer);
            }
        }

        [Fact]
        public void ParseHexa_WithPrefix()
        {
            Assert.Equal(new byte[] { 0x00 }, HexaConverter.ParseHexaString("0x00"));
            Assert.Equal(new byte[] { 0x01 }, HexaConverter.ParseHexaString("0x01"));
            Assert.Equal(new byte[] { 0x02 }, HexaConverter.ParseHexaString("0x02"));
            Assert.Equal(new byte[] { 0x03 }, HexaConverter.ParseHexaString("0x03"));
            Assert.Equal(new byte[] { 0x04 }, HexaConverter.ParseHexaString("0x04"));
            Assert.Equal(new byte[] { 0x05 }, HexaConverter.ParseHexaString("0x05"));
            Assert.Equal(new byte[] { 0x06 }, HexaConverter.ParseHexaString("0x06"));
            Assert.Equal(new byte[] { 0x07 }, HexaConverter.ParseHexaString("0x07"));
            Assert.Equal(new byte[] { 0x08 }, HexaConverter.ParseHexaString("0x08"));
            Assert.Equal(new byte[] { 0x09 }, HexaConverter.ParseHexaString("0x09"));
            Assert.Equal(new byte[] { 0x0A }, HexaConverter.ParseHexaString("0x0a"));
            Assert.Equal(new byte[] { 0x0B }, HexaConverter.ParseHexaString("0x0b"));
            Assert.Equal(new byte[] { 0x0C }, HexaConverter.ParseHexaString("0x0c"));
            Assert.Equal(new byte[] { 0x0D }, HexaConverter.ParseHexaString("0x0d"));
            Assert.Equal(new byte[] { 0x0E }, HexaConverter.ParseHexaString("0x0e"));
            Assert.Equal(new byte[] { 0x0F }, HexaConverter.ParseHexaString("0x0f"));
            Assert.Equal(new byte[] { 0x0A }, HexaConverter.ParseHexaString("0x0A"));
            Assert.Equal(new byte[] { 0x0B }, HexaConverter.ParseHexaString("0x0B"));
            Assert.Equal(new byte[] { 0x0C }, HexaConverter.ParseHexaString("0x0C"));
            Assert.Equal(new byte[] { 0x0D }, HexaConverter.ParseHexaString("0x0D"));
            Assert.Equal(new byte[] { 0x0E }, HexaConverter.ParseHexaString("0x0E"));
            Assert.Equal(new byte[] { 0x0F }, HexaConverter.ParseHexaString("0x0F"));

            Assert.Equal(new byte[] { 0x4F, 0xAF, 0x65 }, HexaConverter.ParseHexaString("0x4Faf65"));
        }

        [Fact]
        public void ParseHexa_InvalidCharacters()
        {
            Assert.Throws<ArgumentException>(() => HexaConverter.ParseHexaString("0H"));
        }

        [Fact]
        public void ParseHexa_InvalidLength()
        {
            Assert.Throws<ArgumentException>(() => HexaConverter.ParseHexaString("000"));
        }

        [Fact]
        public void ToHexa_Span_UpperCase()
        {
            var options = HexaOptions.UpperCase;
            Assert.Equal("00", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.Equal("01", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.Equal("02", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.Equal("03", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.Equal("04", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.Equal("05", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.Equal("06", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.Equal("07", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.Equal("08", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.Equal("09", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.Equal("0A", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.Equal("0B", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.Equal("0C", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.Equal("0D", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.Equal("0E", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.Equal("0F", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.Equal("102F", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
        public void ToHexa_Span_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.Equal("00", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.Equal("01", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.Equal("02", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.Equal("03", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.Equal("04", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.Equal("05", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.Equal("06", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.Equal("07", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.Equal("08", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.Equal("09", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.Equal("0a", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.Equal("0b", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.Equal("0c", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.Equal("0d", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.Equal("0e", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.Equal("0f", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.Equal("102f", HexaConverter.ToHexaString((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
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
                Assert.True(HexaConverter.TryParseHexaString(str, buffer, out var writtenBytes));
                Assert.Equal(expected, buffer.ToArray());
                Assert.Equal(buffer.Length, writtenBytes);
            }
        }

        [Fact]
        public void TryParseHexa_Span_WithPrefix()
        {
            Assert.Equal(new byte[] { 0x00 }, HexaConverter.ParseHexaString("0x00"));
            Assert.Equal(new byte[] { 0x01 }, HexaConverter.ParseHexaString("0x01"));
            Assert.Equal(new byte[] { 0x02 }, HexaConverter.ParseHexaString("0x02"));
            Assert.Equal(new byte[] { 0x03 }, HexaConverter.ParseHexaString("0x03"));
            Assert.Equal(new byte[] { 0x04 }, HexaConverter.ParseHexaString("0x04"));
            Assert.Equal(new byte[] { 0x05 }, HexaConverter.ParseHexaString("0x05"));
            Assert.Equal(new byte[] { 0x06 }, HexaConverter.ParseHexaString("0x06"));
            Assert.Equal(new byte[] { 0x07 }, HexaConverter.ParseHexaString("0x07"));
            Assert.Equal(new byte[] { 0x08 }, HexaConverter.ParseHexaString("0x08"));
            Assert.Equal(new byte[] { 0x09 }, HexaConverter.ParseHexaString("0x09"));
            Assert.Equal(new byte[] { 0x0A }, HexaConverter.ParseHexaString("0x0a"));
            Assert.Equal(new byte[] { 0x0B }, HexaConverter.ParseHexaString("0x0b"));
            Assert.Equal(new byte[] { 0x0C }, HexaConverter.ParseHexaString("0x0c"));
            Assert.Equal(new byte[] { 0x0D }, HexaConverter.ParseHexaString("0x0d"));
            Assert.Equal(new byte[] { 0x0E }, HexaConverter.ParseHexaString("0x0e"));
            Assert.Equal(new byte[] { 0x0F }, HexaConverter.ParseHexaString("0x0f"));
            Assert.Equal(new byte[] { 0x0A }, HexaConverter.ParseHexaString("0x0A"));
            Assert.Equal(new byte[] { 0x0B }, HexaConverter.ParseHexaString("0x0B"));
            Assert.Equal(new byte[] { 0x0C }, HexaConverter.ParseHexaString("0x0C"));
            Assert.Equal(new byte[] { 0x0D }, HexaConverter.ParseHexaString("0x0D"));
            Assert.Equal(new byte[] { 0x0E }, HexaConverter.ParseHexaString("0x0E"));
            Assert.Equal(new byte[] { 0x0F }, HexaConverter.ParseHexaString("0x0F"));

            Assert.Equal(new byte[] { 0x4F, 0xAF, 0x65 }, HexaConverter.ParseHexaString("0x4Faf65"));
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
            Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> bytes = new byte[10];
                HexaConverter.ParseHexaString("000");
            });
        }
    }
}
