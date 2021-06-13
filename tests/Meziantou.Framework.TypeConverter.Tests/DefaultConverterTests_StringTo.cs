using System;
using System.Globalization;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_StringTo
    {
        [Flags]
        private enum SampleEnum
        {
            None = 0x0,
            Option1 = 0x1,
            Option2 = 0x2,
            Option3 = 0x4,
        }

        [Fact]
        public void TryConvert_StringToInt32_ValidValue()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int value);

            converted.Should().BeTrue();
            value.Should().Be(42);
        }

        [Fact]
        public void TryConvert_StringToInt32_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out int _);

            converted.Should().BeFalse();
        }

        [Fact]
        public void TryConvert_StringToNullableInt32_ValidInteger()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42", cultureInfo, out int? value);

            converted.Should().BeTrue();
            value.Should().Be(42);
        }

        [Fact]
        public void TryConvert_StringToNullableInt32_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out int? value);

            converted.Should().BeTrue();
            value.Should().BeNull();
        }

        [Fact]
        public void TryConvert_StringToEnum_ValueAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2", cultureInfo, out SampleEnum value);

            converted.Should().BeTrue();
            value.Should().Be(SampleEnum.Option2);
        }

        [Fact]
        public void TryConvert_StringToEnum_CommaSeparatedString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("Option1, Option2", cultureInfo, out SampleEnum value);

            converted.Should().BeTrue();
            value.Should().Be(SampleEnum.Option1 | SampleEnum.Option2);
        }

        [Fact]
        public void TryConvert_StringToEnum_CommaSeparatedStringAndInt()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("Option1, 2", cultureInfo, out SampleEnum value);

            converted.Should().BeTrue();
            value.Should().Be(SampleEnum.Option1 | SampleEnum.Option2);
        }

        [Fact]
        public void TryConvert_StringToNullableLong()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("3000000000", cultureInfo, out long? value);

            converted.Should().BeTrue();
            value.Should().Be(3000000000L);
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_CultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("fr-FR", cultureInfo, out CultureInfo value);

            converted.Should().BeTrue();
            value.Name.Should().Be("fr-FR");
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_NeutralCultureAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("es", cultureInfo, out CultureInfo value);

            converted.Should().BeTrue();
            value.Name.Should().Be("es");
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_LcidAsString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("1033", cultureInfo, out CultureInfo value);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                converted.Should().BeTrue();
                value.Name.Should().Be("en-US");
            }
            else
            {
                converted.Should().BeFalse();
            }
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_InvalidCulture()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("dfgnksdfklgfg", cultureInfo, out CultureInfo _);

            converted.Should().BeFalse();
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out CultureInfo value);

            converted.Should().BeTrue();
            value.Should().Be(CultureInfo.InvariantCulture);
        }

        [Fact]
        public void TryConvert_StringToCultureInfo_NullValue()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            string inputValue = null;
            var converted = converter.TryChangeType<CultureInfo>(inputValue, cultureInfo, out var value);

            converted.Should().BeTrue();
            value.Should().BeNull();
        }

        [Fact]
        public void TryConvert_StringToUri_EmptyString()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("", cultureInfo, out Uri value);

            converted.Should().BeTrue();
            value.Should().BeNull();
        }

        [Fact]
        public void TryConvert_StringToUri_RelativeUri()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("test.png", cultureInfo, out Uri value);

            converted.Should().BeTrue();
            value.Should().Be(new Uri("test.png", UriKind.Relative));
        }

        [Fact]
        public void TryConvert_StringToUri_AbsoluteUri()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("https://meziantou.net", cultureInfo, out Uri value);

            converted.Should().BeTrue();
            value.Should().Be(new Uri("https://meziantou.net", UriKind.Absolute));
        }

        [Fact]
        public void TryConvert_StringToTimeSpan()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("12:30", cultureInfo, out TimeSpan value);

            converted.Should().BeTrue();
            value.Should().Be(new TimeSpan(12, 30, 0));
        }

        [Fact]
        public void TryConvert_StringToGuid()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2d8a54aa-569b-404f-933b-693918885dba", cultureInfo, out Guid value);

            converted.Should().BeTrue();
            value.Should().Be(new Guid("2d8a54aa-569b-404f-933b-693918885dba"));
        }

        [Fact]
        public void TryConvert_StringToDecimal()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("42.24", cultureInfo, out decimal value);

            converted.Should().BeTrue();
            value.Should().Be(42.24m);
        }

        [Fact]
        public void TryConvert_StringToByteArray_Base16_Prefixed()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0x0AFF", cultureInfo, out byte[] value);

            converted.Should().BeTrue();
            value.Should().Equal(new byte[] { 0x0A, 0xFF });
        }

        [Fact]
        public void TryConvert_StringToByteArray_Base64()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("AQIDBA==", cultureInfo, out byte[] value);

            converted.Should().BeTrue();
            value.Should().Equal(new byte[] { 1, 2, 3, 4 });
        }

        [Fact]
        public void TryConvert_StringToByteArray_Base64_Invalid()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("AQIDBA=", cultureInfo, out byte[] _);

            converted.Should().BeFalse();
        }

        [Fact]
        public void TryConvert_StringToDateTime()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2018/06/24 14:21:01", cultureInfo, out DateTime value);

            converted.Should().BeTrue();
            value.Should().Be(new DateTime(2018, 06, 24, 14, 21, 01));
        }

        [Fact]
        public void TryConvert_StringToDateTimeOffset()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("2018/06/24 14:21:01+0230", cultureInfo, out DateTimeOffset value);

            converted.Should().BeTrue();
            value.Should().Be(new DateTimeOffset(2018, 06, 24, 14, 21, 01, new TimeSpan(2, 30, 0)));
        }

        [Fact]
        public void TryConvert_StringToByteArray_Base16()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0d0102", cultureInfo, out byte[] value);

            converted.Should().BeTrue();
            value.Should().Equal(new byte[] { 0x0d, 0x01, 0x02 });
        }

        [Fact]
        public void TryConvert_StringToByteArray_Base16Prefixed()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType("0x0d01", cultureInfo, out byte[] value);

            converted.Should().BeTrue();
            value.Should().Equal(new byte[] { 0x0d, 0x01 });
        }

        [Theory]
        [InlineData("True")]
        [InlineData("true")]
        [InlineData("t")]
        [InlineData("yes")]
        [InlineData("y")]
        public void TryConvert_StringToBoolean_TrueValue(string text)
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(text, cultureInfo, out bool value);

            converted.Should().BeTrue();
            value.Should().BeTrue();
        }

        [Theory]
        [InlineData("False")]
        [InlineData("false")]
        [InlineData("f")]
        [InlineData("No")]
        [InlineData("n")]
        public void TryConvert_StringToBoolean_FalseValue(string text)
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(text, cultureInfo, out bool value);

            converted.Should().BeTrue();
            value.Should().BeFalse();
        }
    }
}
