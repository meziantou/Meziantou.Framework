using System;
using System.Globalization;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DefaultConverterTests_StringTo
    {
        [Flags]
        private enum SampleEnum
        {
            None = 0x0,
            Option1 = 0x1,
            Option2 = 0x2,
            Option3 = 0x4
        }

        [TestMethod]
        public void TryConvert_StringToInt32_ValidValue()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(42, value);
        }

        [TestMethod]
        public void TryConvert_StringToInt32_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out int value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_StringToNullableInt32_ValidInteger()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(42, value);
        }

        [TestMethod]
        public void TryConvert_StringToNullableInt32_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out int? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_StringToEnum_ValueAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2", cultureInfo, out SampleEnum value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(SampleEnum.Option2, value);
        }

        [TestMethod]
        public void TryConvert_StringToEnum_CommaSeparatedString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("Option1, Option2", cultureInfo, out SampleEnum value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(SampleEnum.Option1 | SampleEnum.Option2, value);
        }

        [TestMethod]
        public void TryConvert_StringToEnum_CommaSeparatedStringAndInt()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("Option1, 2", cultureInfo, out SampleEnum value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(SampleEnum.Option1 | SampleEnum.Option2, value);
        }

        [TestMethod]
        public void TryConvert_StringToNullableLong()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("3000000000", cultureInfo, out long? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(3000000000L, value);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_CultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("fr-FR", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("fr-FR", value.Name);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_NeutralCultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("es", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("es", value.Name);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_LcidAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("1033", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("en-US", value.Name);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_InvalidCulture()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("dfgnksdfklgfg", cultureInfo, out CultureInfo value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(CultureInfo.InvariantCulture, value);
        }

        [TestMethod]
        public void TryConvert_StringToCultureInfo_NullValue()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            string inputValue = null;
            var converted = converter.TryChangeType<CultureInfo>(inputValue, cultureInfo, out var value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_StringToUri_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            // Different behavior in .NET461 and .NET Core 2
#if NET461
            Assert.AreEqual("", value.ToString());
#else
            Assert.AreEqual(null, value);
#endif
        }

        [TestMethod]
        public void TryConvert_StringToUri_RelativeUri()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("test.png", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new Uri("test.png", UriKind.Relative), value);
        }

        [TestMethod]
        public void TryConvert_StringToUri_AbsoluteUri()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("https://meziantou.net", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new Uri("https://meziantou.net", UriKind.Absolute), value);
        }

        [TestMethod]
        public void TryConvert_StringToTimeSpan()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("12:30", cultureInfo, out TimeSpan value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new TimeSpan(12, 30, 0), value);
        }

        [TestMethod]
        public void TryConvert_StringToGuid()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2d8a54aa-569b-404f-933b-693918885dba", cultureInfo, out Guid value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new Guid("2d8a54aa-569b-404f-933b-693918885dba"), value);
        }

        [TestMethod]
        public void TryConvert_StringToDecimal()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42.24", cultureInfo, out decimal value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(42.24m, value);
        }

        [TestMethod]
        public void TryConvert_StringToByteArray_Base16_Prefixed()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0x0AFF", cultureInfo, out byte[] value);

            Assert.AreEqual(true, converted);
            CollectionAssert.AreEqual(new byte[] { 0x0A, 0xFF }, value);
        }

        [TestMethod]
        public void TryConvert_StringToByteArray_Base64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("AQIDBA==", cultureInfo, out byte[] value);

            Assert.AreEqual(true, converted);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, value);
        }

        [TestMethod]
        public void TryConvert_StringToByteArray_Base64_Invalid()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("AQIDBA=", cultureInfo, out byte[] value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_StringToDateTime()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2018/06/24 14:21:01", cultureInfo, out DateTime value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new DateTime(2018, 06, 24, 14, 21, 01), value);
        }

        [TestMethod]
        public void TryConvert_StringToDateTimeOffset()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2018/06/24 14:21:01+0230", cultureInfo, out DateTimeOffset value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new DateTimeOffset(2018, 06, 24, 14, 21, 01, new TimeSpan(2, 30, 0)), value);
        }

        [TestMethod]
        public void TryConvert_StringToByteArray_Base16()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0d0102", cultureInfo, out byte[] value);

            Assert.AreEqual(true, converted);
            CollectionAssert.AreEqual(new byte[] { 0x0d, 0x01, 0x02 }, value);
        }

        [TestMethod]
        public void TryConvert_StringToByteArray_Base16Prefixed()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0x0d01", cultureInfo, out byte[] value);

            Assert.AreEqual(true, converted);
            CollectionAssert.AreEqual(new byte[] { 0x0d, 0x01 }, value);
        }

        [DataTestMethod]
        [DataRow("True")]
        [DataRow("true")]
        [DataRow("t")]
        [DataRow("yes")]
        [DataRow("y")]
        public void TryConvert_StringToBoolean_TrueValue(string text)
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(text, cultureInfo, out bool value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(true, value);
        }

        [DataTestMethod]
        [DataRow("False")]
        [DataRow("false")]
        [DataRow("f")]
        [DataRow("No")]
        [DataRow("n")]
        public void TryConvert_StringToBoolean_FalseValue(string text)
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(text, cultureInfo, out bool value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(false, value);
        }
    }
}
