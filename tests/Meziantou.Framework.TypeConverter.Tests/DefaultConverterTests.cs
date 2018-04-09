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
        public void TryConvert_CultureInfo_CultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("fr-FR", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("fr-FR", value.Name);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_NeutralCultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("es", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("es", value.Name);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_LcidAsInt()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1033, cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("en-US", value.Name);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_LcidAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("1033", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual("en-US", value.Name);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_InvalidCulture()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("dfgnksdfklgfg", cultureInfo, out CultureInfo value);

            Assert.AreEqual(false, converted);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out CultureInfo value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(CultureInfo.InvariantCulture, value);
        }

        [TestMethod]
        public void TryConvert_CultureInfo_NullValue()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType<CultureInfo>(null, null, out var value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(null, value);
        }

        [TestMethod]
        public void TryConvert_CultureInfoToStringInvariant()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, CultureInfo.InvariantCulture);

            Assert.AreEqual("en", value);
        }

        [TestMethod]
        public void TryConvert_CultureInfoToString()
        {
            var converter = new DefaultConverter();
            var value = converter.ChangeType<string>(new CultureInfo("en"), null, new CultureInfo("en-US"));

            Assert.AreEqual("en", value);
        }

        [TestMethod]
        public void TryConvert_Uri_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            // Different behavior in .NET461 and .NET Core 2
            Assert.IsTrue(string.IsNullOrEmpty(value?.ToString()));
        }

        [TestMethod]
        public void TryConvert_Uri_02()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("test.png", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new Uri("test.png", UriKind.Relative), value);
        }

        [TestMethod]
        public void TryConvert_Uri_03()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("http://meziantou.net", cultureInfo, out Uri value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(new Uri("http://meziantou.net", UriKind.Absolute), value);
        }
    }
}
