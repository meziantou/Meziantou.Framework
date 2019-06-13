using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ByteArrayExtensionsTests
    {
        [TestMethod]
        public void ToHexa_UpperCase()
        {
            var options = HexaOptions.UpperCase;
            Assert.AreEqual("00", ByteArrayExtensions.ToHexa(new byte[] { 0x00 }, options));
            Assert.AreEqual("01", ByteArrayExtensions.ToHexa(new byte[] { 0x01 }, options));
            Assert.AreEqual("02", ByteArrayExtensions.ToHexa(new byte[] { 0x02 }, options));
            Assert.AreEqual("03", ByteArrayExtensions.ToHexa(new byte[] { 0x03 }, options));
            Assert.AreEqual("04", ByteArrayExtensions.ToHexa(new byte[] { 0x04 }, options));
            Assert.AreEqual("05", ByteArrayExtensions.ToHexa(new byte[] { 0x05 }, options));
            Assert.AreEqual("06", ByteArrayExtensions.ToHexa(new byte[] { 0x06 }, options));
            Assert.AreEqual("07", ByteArrayExtensions.ToHexa(new byte[] { 0x07 }, options));
            Assert.AreEqual("08", ByteArrayExtensions.ToHexa(new byte[] { 0x08 }, options));
            Assert.AreEqual("09", ByteArrayExtensions.ToHexa(new byte[] { 0x09 }, options));
            Assert.AreEqual("0A", ByteArrayExtensions.ToHexa(new byte[] { 0x0A }, options));
            Assert.AreEqual("0B", ByteArrayExtensions.ToHexa(new byte[] { 0x0B }, options));
            Assert.AreEqual("0C", ByteArrayExtensions.ToHexa(new byte[] { 0x0C }, options));
            Assert.AreEqual("0D", ByteArrayExtensions.ToHexa(new byte[] { 0x0D }, options));
            Assert.AreEqual("0E", ByteArrayExtensions.ToHexa(new byte[] { 0x0E }, options));
            Assert.AreEqual("0F", ByteArrayExtensions.ToHexa(new byte[] { 0x0F }, options));
            Assert.AreEqual("102F", ByteArrayExtensions.ToHexa(new byte[] { 0x10, 0x2F }, options));
        }

        [TestMethod]
        public void ToHexa_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.AreEqual("00", ByteArrayExtensions.ToHexa(new byte[] { 0x00 }, options));
            Assert.AreEqual("01", ByteArrayExtensions.ToHexa(new byte[] { 0x01 }, options));
            Assert.AreEqual("02", ByteArrayExtensions.ToHexa(new byte[] { 0x02 }, options));
            Assert.AreEqual("03", ByteArrayExtensions.ToHexa(new byte[] { 0x03 }, options));
            Assert.AreEqual("04", ByteArrayExtensions.ToHexa(new byte[] { 0x04 }, options));
            Assert.AreEqual("05", ByteArrayExtensions.ToHexa(new byte[] { 0x05 }, options));
            Assert.AreEqual("06", ByteArrayExtensions.ToHexa(new byte[] { 0x06 }, options));
            Assert.AreEqual("07", ByteArrayExtensions.ToHexa(new byte[] { 0x07 }, options));
            Assert.AreEqual("08", ByteArrayExtensions.ToHexa(new byte[] { 0x08 }, options));
            Assert.AreEqual("09", ByteArrayExtensions.ToHexa(new byte[] { 0x09 }, options));
            Assert.AreEqual("0a", ByteArrayExtensions.ToHexa(new byte[] { 0x0A }, options));
            Assert.AreEqual("0b", ByteArrayExtensions.ToHexa(new byte[] { 0x0B }, options));
            Assert.AreEqual("0c", ByteArrayExtensions.ToHexa(new byte[] { 0x0C }, options));
            Assert.AreEqual("0d", ByteArrayExtensions.ToHexa(new byte[] { 0x0D }, options));
            Assert.AreEqual("0e", ByteArrayExtensions.ToHexa(new byte[] { 0x0E }, options));
            Assert.AreEqual("0f", ByteArrayExtensions.ToHexa(new byte[] { 0x0F }, options));
            Assert.AreEqual("102f", ByteArrayExtensions.ToHexa(new byte[] { 0x10, 0x2F }, options));
        }

        [TestMethod]
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
                Assert.IsTrue(ByteArrayExtensions.TryParseHexa(str, out byte[] buffer));
                CollectionAssert.AreEqual(expected, buffer);
            }
        }

        [TestMethod]
        public void ParseHexa_WithPrefix()
        {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, ByteArrayExtensions.ParseHexa("0x00"));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, ByteArrayExtensions.ParseHexa("0x01"));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, ByteArrayExtensions.ParseHexa("0x02"));
            CollectionAssert.AreEqual(new byte[] { 0x03 }, ByteArrayExtensions.ParseHexa("0x03"));
            CollectionAssert.AreEqual(new byte[] { 0x04 }, ByteArrayExtensions.ParseHexa("0x04"));
            CollectionAssert.AreEqual(new byte[] { 0x05 }, ByteArrayExtensions.ParseHexa("0x05"));
            CollectionAssert.AreEqual(new byte[] { 0x06 }, ByteArrayExtensions.ParseHexa("0x06"));
            CollectionAssert.AreEqual(new byte[] { 0x07 }, ByteArrayExtensions.ParseHexa("0x07"));
            CollectionAssert.AreEqual(new byte[] { 0x08 }, ByteArrayExtensions.ParseHexa("0x08"));
            CollectionAssert.AreEqual(new byte[] { 0x09 }, ByteArrayExtensions.ParseHexa("0x09"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0a"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0b"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0c"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0d"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0e"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0f"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0A"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0B"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0C"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0D"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0E"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0F"));

            CollectionAssert.AreEqual(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.ParseHexa("0x4Faf65"));
        }

        [TestMethod]
        public void ParseHexa_InvalidCharacters()
        {
            Assert.ThrowsException<ArgumentException>(() => ByteArrayExtensions.ParseHexa("0H"));
        }

        [TestMethod]
        public void ParseHexa_InvalidLength()
        {
            Assert.ThrowsException<ArgumentException>(() => ByteArrayExtensions.ParseHexa("000"));
        }

#if NETCOREAPP2_1
        [TestMethod]
        public void ToHexa_Span_UpperCase()
        {
            var options = HexaOptions.UpperCase;
            Assert.AreEqual("00", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.AreEqual("01", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.AreEqual("02", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.AreEqual("03", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.AreEqual("04", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.AreEqual("05", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.AreEqual("06", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.AreEqual("07", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.AreEqual("08", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.AreEqual("09", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.AreEqual("0A", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.AreEqual("0B", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.AreEqual("0C", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.AreEqual("0D", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.AreEqual("0E", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.AreEqual("0F", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.AreEqual("102F", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
        }

        [TestMethod]
        public void ToHexa_Span_LowerCase()
        {
            var options = HexaOptions.LowerCase;
            Assert.AreEqual("00", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x00 }, options));
            Assert.AreEqual("01", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x01 }, options));
            Assert.AreEqual("02", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x02 }, options));
            Assert.AreEqual("03", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x03 }, options));
            Assert.AreEqual("04", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x04 }, options));
            Assert.AreEqual("05", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x05 }, options));
            Assert.AreEqual("06", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x06 }, options));
            Assert.AreEqual("07", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x07 }, options));
            Assert.AreEqual("08", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x08 }, options));
            Assert.AreEqual("09", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x09 }, options));
            Assert.AreEqual("0a", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0A }, options));
            Assert.AreEqual("0b", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0B }, options));
            Assert.AreEqual("0c", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0C }, options));
            Assert.AreEqual("0d", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0D }, options));
            Assert.AreEqual("0e", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0E }, options));
            Assert.AreEqual("0f", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x0F }, options));
            Assert.AreEqual("102f", ByteArrayExtensions.ToHexa((ReadOnlySpan<byte>)new byte[] { 0x10, 0x2F }, options));
        }

        [TestMethod]
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
                Assert.IsTrue(ByteArrayExtensions.TryParseHexa(str, buffer));
                CollectionAssert.AreEqual(expected, buffer.ToArray());
            }
        }

        [TestMethod]
        public void TryParseHexa_Span_WithPrefix()
        {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, ByteArrayExtensions.ParseHexa("0x00"));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, ByteArrayExtensions.ParseHexa("0x01"));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, ByteArrayExtensions.ParseHexa("0x02"));
            CollectionAssert.AreEqual(new byte[] { 0x03 }, ByteArrayExtensions.ParseHexa("0x03"));
            CollectionAssert.AreEqual(new byte[] { 0x04 }, ByteArrayExtensions.ParseHexa("0x04"));
            CollectionAssert.AreEqual(new byte[] { 0x05 }, ByteArrayExtensions.ParseHexa("0x05"));
            CollectionAssert.AreEqual(new byte[] { 0x06 }, ByteArrayExtensions.ParseHexa("0x06"));
            CollectionAssert.AreEqual(new byte[] { 0x07 }, ByteArrayExtensions.ParseHexa("0x07"));
            CollectionAssert.AreEqual(new byte[] { 0x08 }, ByteArrayExtensions.ParseHexa("0x08"));
            CollectionAssert.AreEqual(new byte[] { 0x09 }, ByteArrayExtensions.ParseHexa("0x09"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0a"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0b"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0c"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0d"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0e"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0f"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.ParseHexa("0x0A"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.ParseHexa("0x0B"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.ParseHexa("0x0C"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.ParseHexa("0x0D"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.ParseHexa("0x0E"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.ParseHexa("0x0F"));

            CollectionAssert.AreEqual(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.ParseHexa("0x4Faf65"));
        }

        [TestMethod]
        public void TryParseHexa_Span_InvalidCharacters()
        {
            Span<byte> bytes = new byte[10];
            Assert.IsFalse(ByteArrayExtensions.TryParseHexa("0H", bytes));
        }

        [TestMethod]
        public void TryHexa_Span_BufferTooSmall()
        {
            Span<byte> bytes = new byte[10];
            Assert.IsFalse(ByteArrayExtensions.TryParseHexa("000", bytes));
        }

        [TestMethod]
        public void TryHexa_Span_InvalidLength()
        {
            Assert.ThrowsException<ArgumentException>(() =>
            {
                Span<byte> bytes = new byte[10];
                ByteArrayExtensions.ParseHexa("000");
            });
        }
#endif
    }
}
