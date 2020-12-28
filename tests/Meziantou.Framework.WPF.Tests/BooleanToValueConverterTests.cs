using Xunit;

namespace Meziantou.Framework.WPF.Tests
{
    public sealed class BooleanToValueConverterTests
    {
        private readonly BooleanToValueConverter _converter = new()
        {
            TrueValue = "true",
            FalseValue = "false",
            NullValue = "null",
        };

        private object Convert(object value)
        {
            return _converter.Convert(value, typeof(object), parameter: null, culture: null);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, "true")]
        [InlineData(true, 1)]
        [InlineData(true, 1u)]
        [InlineData(true, -1)]
        [InlineData(false, false)]
        [InlineData(false, 0)]
        [InlineData(false, "false")]
        [InlineData(null, null)]
        [InlineData(null, "")]
        [InlineData(null, "abc")]
        public void Test(bool? expectedValue, object value)
        {
            var expected = expectedValue switch
            {
                null => _converter.NullValue,
                false => _converter.FalseValue,
                true => _converter.TrueValue,
            };

            Assert.Equal(expected, Convert(value));
        }

        [Fact]
        public void FallbackToFalseValue()
        {
            _converter.NullValue = null;

            Assert.Equal(_converter.FalseValue, Convert(null));
        }
    }
}
