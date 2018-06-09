using System;
using System.Globalization;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DefaultConverterTests
    {
        [Flags]
        private enum SampleEnum
        {
            None = 0x0,
            Option1 = 0x1,
            Option2 = 0x2,
            Option3 = 0x4
        }

        private class ImplicitConverter
        {
            public static implicit operator int(ImplicitConverter _)
            {
                return 1;
            }
        }

        [TestMethod]
        public void TryConvert_ImplicitConverter_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new ImplicitConverter(), cultureInfo, out int value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(1, value);
        }

        [TestMethod]
        public void TryConvert_DbNullToNullableInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_DbNullToInt32()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_DbNullToString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out string value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_StringToInt32_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(42, value);
        }

        [TestMethod]
        public void TryConvert_StringToInt32_02()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out int value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_StringToNullableInt32_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int? value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(42, value);
        }

        [TestMethod]
        public void TryConvert_StringToNullableInt32_02()
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
        public void TryConvert_StringToGuid_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("afa523e478df4b2da8c86d6b43c48e3e", cultureInfo, out Guid value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(Guid.Parse("afa523e478df4b2da8c86d6b43c48e3e"), value);
        }

        [TestMethod]
        public void TryConvert_StringToNullableLong_01()
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
        public void TryConvert_Int32ToCultureInfo_LcidAsInt()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1033, cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("en-US", value.Name);
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
        public void TryConvert_CultureInfoToString_UsingInvariantCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, CultureInfo.InvariantCulture);

            Assert.AreEqual("en", value);
        }

        [TestMethod]
        public void TryConvert_CultureInfoToString_UsingSpecificCulture()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, new CultureInfo("en-US"));

            Assert.AreEqual("en", value);
        }

        [TestMethod]
        public void TryConvert_StringToUri_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            // Different behavior in .NET461 and .NET Core 2
            Assert.IsTrue(string.IsNullOrEmpty(value?.ToString()));
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
        public void TryConvert_ByteArrayToString_Base64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("AQIDBA==", value);
        }

        [TestMethod]
        public void TryConvert_ByteArrayToString_Base16WithPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16Prefixed;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("0x01020304", value);
        }

        [TestMethod]
        public void TryConvert_ByteArrayToString_Base16WithoutPrefix()
        {
            var converter = new DefaultConverter();
            converter.ByteArrayToStringFormat = ByteArrayToStringFormat.Base16;
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new byte[] { 1, 2, 3, 4 }, cultureInfo, out string value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("01020304", value);
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
        public void TryConvert_StringToDateTime()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2018/09/24", cultureInfo, out DateTime value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new DateTime(2018, 9, 24), value);
        }
    }
}
