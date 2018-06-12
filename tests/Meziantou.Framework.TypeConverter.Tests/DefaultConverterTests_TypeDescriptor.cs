using System;
using System.ComponentModel;
using System.Globalization;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DefaultConverterTests_TypeDescriptor
    {
        private class CustomTypeConverter : System.ComponentModel.TypeConverter
        {
            public static Dummy Instance { get; } = new Dummy();

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(int);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                return Instance;
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(int);
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            {
                return 10;
            }
        }

        [TypeConverter(typeof(CustomTypeConverter))]
        private class Dummy
        {
        }

        [TestMethod]
        public void TryConvert_TypeConverter_ConvertTo()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new Dummy(), cultureInfo, out int value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(10, value);
        }

        [TestMethod]
        public void TryConvert_TypeConverter_ConvertFrom()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1, cultureInfo, out Dummy value);

            Assert.AreEqual(true, converted);
            Assert.AreEqual(CustomTypeConverter.Instance, value);
        }

        [TestMethod]
        public void TryConvert_TypeConverter_ConvertFrom_NoMatchingTypeConverter()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out Dummy value);

            Assert.AreEqual(false, converted);
        }
    }
}
