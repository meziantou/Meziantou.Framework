using System;
using System.ComponentModel;
using System.Globalization;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class DefaultConverterTests_TypeDescriptor
    {
        private sealed class CustomTypeConverter : TypeConverter
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

        [Fact]
        public void TryConvert_TypeConverter_ConvertTo()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new Dummy(), cultureInfo, out int value);

            Assert.True(converted);
            Assert.Equal(10, value);
        }

        [Fact]
        public void TryConvert_TypeConverter_ConvertFrom()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1, cultureInfo, out Dummy value);

            Assert.True(converted);
            Assert.Equal(CustomTypeConverter.Instance, value);
        }

        [Fact]
        public void TryConvert_TypeConverter_ConvertFrom_NoMatchingTypeConverter()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;

            var converted = converter.TryChangeType("", cultureInfo, out Dummy _);

            Assert.False(converted);
        }
    }
}
