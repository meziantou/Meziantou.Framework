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
        public void FromHexa()
        {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, ByteArrayExtensions.FromHexa("00"));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, ByteArrayExtensions.FromHexa("01"));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, ByteArrayExtensions.FromHexa("02"));
            CollectionAssert.AreEqual(new byte[] { 0x03 }, ByteArrayExtensions.FromHexa("03"));
            CollectionAssert.AreEqual(new byte[] { 0x04 }, ByteArrayExtensions.FromHexa("04"));
            CollectionAssert.AreEqual(new byte[] { 0x05 }, ByteArrayExtensions.FromHexa("05"));
            CollectionAssert.AreEqual(new byte[] { 0x06 }, ByteArrayExtensions.FromHexa("06"));
            CollectionAssert.AreEqual(new byte[] { 0x07 }, ByteArrayExtensions.FromHexa("07"));
            CollectionAssert.AreEqual(new byte[] { 0x08 }, ByteArrayExtensions.FromHexa("08"));
            CollectionAssert.AreEqual(new byte[] { 0x09 }, ByteArrayExtensions.FromHexa("09"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.FromHexa("0a"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.FromHexa("0b"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.FromHexa("0c"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.FromHexa("0d"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.FromHexa("0e"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.FromHexa("0f"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.FromHexa("0A"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.FromHexa("0B"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.FromHexa("0C"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.FromHexa("0D"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.FromHexa("0E"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.FromHexa("0F"));

            CollectionAssert.AreEqual(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.FromHexa("4Faf65"));
        }

        [TestMethod]
        public void FromHexa_WithPrefix()
        {
            CollectionAssert.AreEqual(new byte[] { 0x00 }, ByteArrayExtensions.FromHexa("0x00"));
            CollectionAssert.AreEqual(new byte[] { 0x01 }, ByteArrayExtensions.FromHexa("0x01"));
            CollectionAssert.AreEqual(new byte[] { 0x02 }, ByteArrayExtensions.FromHexa("0x02"));
            CollectionAssert.AreEqual(new byte[] { 0x03 }, ByteArrayExtensions.FromHexa("0x03"));
            CollectionAssert.AreEqual(new byte[] { 0x04 }, ByteArrayExtensions.FromHexa("0x04"));
            CollectionAssert.AreEqual(new byte[] { 0x05 }, ByteArrayExtensions.FromHexa("0x05"));
            CollectionAssert.AreEqual(new byte[] { 0x06 }, ByteArrayExtensions.FromHexa("0x06"));
            CollectionAssert.AreEqual(new byte[] { 0x07 }, ByteArrayExtensions.FromHexa("0x07"));
            CollectionAssert.AreEqual(new byte[] { 0x08 }, ByteArrayExtensions.FromHexa("0x08"));
            CollectionAssert.AreEqual(new byte[] { 0x09 }, ByteArrayExtensions.FromHexa("0x09"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.FromHexa("0x0a"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.FromHexa("0x0b"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.FromHexa("0x0c"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.FromHexa("0x0d"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.FromHexa("0x0e"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.FromHexa("0x0f"));
            CollectionAssert.AreEqual(new byte[] { 0x0A }, ByteArrayExtensions.FromHexa("0x0A"));
            CollectionAssert.AreEqual(new byte[] { 0x0B }, ByteArrayExtensions.FromHexa("0x0B"));
            CollectionAssert.AreEqual(new byte[] { 0x0C }, ByteArrayExtensions.FromHexa("0x0C"));
            CollectionAssert.AreEqual(new byte[] { 0x0D }, ByteArrayExtensions.FromHexa("0x0D"));
            CollectionAssert.AreEqual(new byte[] { 0x0E }, ByteArrayExtensions.FromHexa("0x0E"));
            CollectionAssert.AreEqual(new byte[] { 0x0F }, ByteArrayExtensions.FromHexa("0x0F"));

            CollectionAssert.AreEqual(new byte[] { 0x4F, 0xAF, 0x65 }, ByteArrayExtensions.FromHexa("0x4Faf65"));
        }

        [TestMethod]
        public void FromHexa_InvalidCharacters()
        {
            Assert.ThrowsException<ArgumentException>(() => ByteArrayExtensions.FromHexa("0H"));
        }

        [TestMethod]
        public void FromHexa_InvalidLength()
        {
            Assert.ThrowsException<ArgumentException>(() => ByteArrayExtensions.FromHexa("000"));
        }
    }
}
