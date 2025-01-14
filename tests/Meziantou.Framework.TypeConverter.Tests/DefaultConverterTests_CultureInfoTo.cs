using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class DefaultConverterTests_CultureInfoTo
{
    [Fact]
    public void TryConvert_CultureInfoToString_UsingInvariantCulture()
    {
        var converter = new DefaultConverter();
        var value = converter.ChangeType<string>(CultureInfo.GetCultureInfo("en"), defaultValue: null, CultureInfo.InvariantCulture);
        Assert.Equal("en", value);
    }

    [Fact]
    public void TryConvert_CultureInfoToString_UsingSpecificCulture()
    {
        var converter = new DefaultConverter();
        var value = converter.ChangeType<string>(CultureInfo.GetCultureInfo("en"), defaultValue: null, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("en", value);
    }
}
