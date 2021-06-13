﻿using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DefaultConverterTests_ImplicitConverter
    {
        private sealed class ImplicitConverter
        {
            public static implicit operator int(ImplicitConverter _) => 1;
        }

        [Fact]
        public void TryConvert_ImplicitConverter_01()
        {
            var converter = new DefaultConverter();
            var cultureInfo = CultureInfo.InvariantCulture;
            var converted = converter.TryChangeType(new ImplicitConverter(), cultureInfo, out int value);

            converted.Should().BeTrue();
            value.Should().Be(1);
        }
    }
}
