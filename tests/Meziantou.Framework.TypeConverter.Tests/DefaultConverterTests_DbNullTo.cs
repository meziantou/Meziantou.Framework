using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_DbNullTo
{
    [Fact]
    public void TryConvert_DbNullToNullableInt32()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int? value);

        converted.Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public void TryConvert_DbNullToInt32()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out int _);

        converted.Should().BeFalse();
    }

    [Fact]
    public void TryConvert_DbNullToString()
    {
        var converter = new DefaultConverter();
        var cultureInfo = CultureInfo.InvariantCulture;
        var converted = converter.TryChangeType(DBNull.Value, cultureInfo, out string value);

        converted.Should().BeTrue();
        value.Should().BeNull();
    }
}
