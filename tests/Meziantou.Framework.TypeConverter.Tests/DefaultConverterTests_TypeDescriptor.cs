using System.ComponentModel;
using System.Globalization;
using FluentAssertions;
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
        private sealed class Dummy
        {
        }

        [Fact]
        public void TryConvert_TypeConverter_ConvertTo()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new Dummy(), cultureInfo, out int value);

            converted.Should().BeTrue();
            value.Should().Be(10);
        }

        [Fact]
        public void TryConvert_TypeConverter_ConvertFrom()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(1, cultureInfo, out Dummy value);

            converted.Should().BeTrue();
            value.Should().Be(CustomTypeConverter.Instance);
        }

        [Fact]
        public void TryConvert_TypeConverter_ConvertFrom_NoMatchingTypeConverter()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;

            var converted = converter.TryChangeType("", cultureInfo, out Dummy _);

            converted.Should().BeFalse();
        }
    }
}
