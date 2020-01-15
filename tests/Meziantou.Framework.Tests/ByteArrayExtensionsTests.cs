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
            Assert.Equal("00", ByteArrayExtensions.ToHexa(new byte[] { 0x00 }, options));
            Assert.Equal("01", ByteArrayExtensions.ToHexa(new byte[] { 0x01 }, options));
            Assert.Equal("02", ByteArrayExtensions.ToHexa(new byte[] { 0x02 }, options));
            Assert.Equal("03", ByteArrayExtensions.ToHexa(new byte[] { 0x03 }, options));
            Assert.Equal("04", ByteArrayExtensions.ToHexa(new byte[] { 0x04 }, options));
            Assert.Equal("05", ByteArrayExtensions.ToHexa(new byte[] { 0x05 }, options));
            Assert.Equal("06", ByteArrayExtensions.ToHexa(new byte[] { 0x06 }, options));
            Assert.Equal("07", ByteArrayExtensions.ToHexa(new byte[] { 0x07 }, options));
            Assert.Equal("08", ByteArrayExtensions.ToHexa(new byte[] { 0x08 }, options));
            Assert.Equal("09", ByteArrayExtensions.ToHexa(new byte[] { 0x09 }, options));
            Assert.Equal("0A", ByteArrayExtensions.ToHexa(new byte[] { 0x0A }, options));
            Assert.Equal("0B", ByteArrayExtensions.ToHexa(new byte[] { 0x0B }, options));
            Assert.Equal("0C", ByteArrayExtensions.ToHexa(new byte[] { 0x0C }, options));
            Assert.Equal("0D", ByteArrayExtensions.ToHexa(new byte[] { 0x0D }, options));
            Assert.Equal("0E", ByteArrayExtensions.ToHexa(new byte[] { 0x0E }, options));
            Assert.Equal("0F", ByteArrayExtensions.ToHexa(new byte[] { 0x0F }, options));
            Assert.Equal("102F", ByteArrayExtensions.ToHexa(new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
        public void ToHexa_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.Equal("00", ByteArrayExtensions.ToHexa(new byte[] { 0x00 }, options));
            Assert.Equal("01", ByteArrayExtensions.ToHexa(new byte[] { 0x01 }, options));
            Assert.Equal("02", ByteArrayExtensions.ToHexa(new byte[] { 0x02 }, options));
            Assert.Equal("03", ByteArrayExtensions.ToHexa(new byte[] { 0x03 }, options));
            Assert.Equal("04", ByteArrayExtensions.ToHexa(new byte[] { 0x04 }, options));
            Assert.Equal("05", ByteArrayExtensions.ToHexa(new byte[] { 0x05 }, options));
            Assert.Equal("06", ByteArrayExtensions.ToHexa(new byte[] { 0x06 }, options));
            Assert.Equal("07", ByteArrayExtensions.ToHexa(new byte[] { 0x07 }, options));
            Assert.Equal("08", ByteArrayExtensions.ToHexa(new byte[] { 0x08 }, options));
            Assert.Equal("09", ByteArrayExtensions.ToHexa(new byte[] { 0x09 }, options));
            Assert.Equal("0a", ByteArrayExtensions.ToHexa(new byte[] { 0x0A }, options));
            Assert.Equal("0b", ByteArrayExtensions.ToHexa(new byte[] { 0x0B }, options));
            Assert.Equal("0c", ByteArrayExtensions.ToHexa(new byte[] { 0x0C }, options));
            Assert.Equal("0d", ByteArrayExtensions.ToHexa(new byte[] { 0x0D }, options));
            Assert.Equal("0e", ByteArrayExtensions.ToHexa(new byte[] { 0x0E }, options));
            Assert.Equal("0f", ByteArrayExtensions.ToHexa(new byte[] { 0x0F }, options));
            Assert.Equal("102f", ByteArrayExtensions.ToHexa(new byte[] { 0x10, 0x2F }, options));
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
                Assert.True(ByteArrayExtensions.TryParseHexa(str, out byte[] buffer));
                Assert.Equal(expected, buffer);
            }
        }

        [Fact]
        public void ParseHexa_WithPrefix()
        {
            Assert.Equal(new byte[] { 0x00 }, ByteArrayExtensions.ParseHexa("0x00"));
            Assert.Equal(new byte[] { 0x01 }, ByteArrayExtensions.ParseHexa("0x01"));
            Assert.Equal(new byte[] { 0x02 }, ByteArrayExtensions.ParseHexa("0x02"));
            Assert.Equal(new byte[] { 0x03 }, ByteArrayExtensions.ParseHexa("0x03"));
            Assert.Equal(new byte[] { 0x04 }, ByteArrayExtensions.ParseHexa("0x04"));
            Assert.Equal(new byte[] { 0x05 }, ByteArrayExtensions.ParseHexa("0x05"));
            Assert.Equal(new byte[] { 0x06 }, ByteArrayExtensions.ParseHexa("0x06"));
            Assert.Equal(new byte[] { 0x07 }, ByteArrayExtensions.ParseHexa("0x07"));
            Assert.Equal(new byte[] { 0x08 }, ByteArrayExtensions.ParseHexa("0x08"));
            Assert.Equal(new byte[] { 0x09 }, ByteArrayExtensions.ParseHexa("0x09"));
            Assert.Equal(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0a"));
            Assert.Equal(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0b"));
            Assert.Equal(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0c"));
            Assert.Equal(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0d"));
            Assert.Equal(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0e"));
            Assert.Equal(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0f"));
            Assert.Equal(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0A"));
            Assert.Equal(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0B"));
            Assert.Equal(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0C"));
            Assert.Equal(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0D"));
            Assert.Equal(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0E"));
            Assert.Equal(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0F"));

            Assert.Equal(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.ParseHexa("0x4Faf65"));
        }

        [Fact]
        public void ParseHexa_InvalidCharacters()
        {
            Assert.Throws<ArgumentException>(() => ByteArrayExtensions.ParseHexa("0H"));
        }

        [Fact]
        public void ParseHexa_InvalidLength()
        {
            Assert.Throws<ArgumentException>(() => ByteArrayExtensions.ParseHexa("000"));
        }

#if NETCOREAPP3_1
        [Fact]
        public void ToHexa_Span_UpperCase()
        {
            var options = HexaOptions.UpperCase;
            Assert.Equal("00", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.Equal("01", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.Equal("02", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.Equal("03", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.Equal("04", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.Equal("05", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.Equal("06", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.Equal("07", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.Equal("08", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.Equal("09", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.Equal("0A", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.Equal("0B", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.Equal("0C", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.Equal("0D", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.Equal("0E", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.Equal("0F", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.Equal("102F", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
        }

        [Fact]
        public void ToHexa_Span_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.Equal("00", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.Equal("01", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.Equal("02", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.Equal("03", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.Equal("04", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.Equal("05", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.Equal("06", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.Equal("07", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.Equal("08", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.Equal("09", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.Equal("0a", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.Equal("0b", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.Equal("0c", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.Equal("0d", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.Equal("0e", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.Equal("0f", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.Equal("102f", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
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
                Assert.True(ByteArrayExtensions.TryParseHexa(str, buffer));
                Assert.Equal(expected, buffer.ToArray());
            }
        }

        [Fact]
        public void TryParseHexa_Span_WithPrefix()
        {
            Assert.Equal(new byte[] { 0x00 }, ByteArrayExtensions.ParseHexa("0x00"));
            Assert.Equal(new byte[] { 0x01 }, ByteArrayExtensions.ParseHexa("0x01"));
            Assert.Equal(new byte[] { 0x02 }, ByteArrayExtensions.ParseHexa("0x02"));
            Assert.Equal(new byte[] { 0x03 }, ByteArrayExtensions.ParseHexa("0x03"));
            Assert.Equal(new byte[] { 0x04 }, ByteArrayExtensions.ParseHexa("0x04"));
            Assert.Equal(new byte[] { 0x05 }, ByteArrayExtensions.ParseHexa("0x05"));
            Assert.Equal(new byte[] { 0x06 }, ByteArrayExtensions.ParseHexa("0x06"));
            Assert.Equal(new byte[] { 0x07 }, ByteArrayExtensions.ParseHexa("0x07"));
            Assert.Equal(new byte[] { 0x08 }, ByteArrayExtensions.ParseHexa("0x08"));
            Assert.Equal(new byte[] { 0x09 }, ByteArrayExtensions.ParseHexa("0x09"));
            Assert.Equal(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0a"));
            Assert.Equal(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0b"));
            Assert.Equal(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0c"));
            Assert.Equal(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0d"));
            Assert.Equal(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0e"));
            Assert.Equal(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0f"));
            Assert.Equal(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0A"));
            Assert.Equal(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0B"));
            Assert.Equal(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0C"));
            Assert.Equal(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0D"));
            Assert.Equal(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0E"));
            Assert.Equal(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0F"));

            Assert.Equal(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.ParseHexa("0x4Faf65"));
        }

        [Fact]
        public void TryParseHexa_Span_InvalidCharacters()
        {
            Span<byte> bytes = new byte[10];
            Assert.False(ByteArrayExtensions.TryParseHexa("0H", bytes));
        }

        [Fact]
        public void TryHexa_Span_BufferTooSmall()
        {
            Span<byte> bytes = new byte[10];
            Assert.False(ByteArrayExtensions.TryParseHexa("000", bytes));
        }

        [Fact]
        public void TryHexa_Span_InvalidLength()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> bytes = new byte[10];
                ByteArrayExtensions.ParseHexa("000");
            });
        }
#elif NET461 || NETSTANDARD2_0
#else
#error Platform not supported
#endif
    }
}
