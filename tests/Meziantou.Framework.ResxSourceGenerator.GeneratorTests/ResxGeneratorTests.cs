#pragma warning disable CA1304
#pragma warning disable MA0011
using System.Globalization;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.ResxSourceGenerator.GeneratorTests;

public class ResxGeneratorTests
{
    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void FormatString()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("Hello world!", Resource1.FormatHello("world"));

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
        Assert.Equal("Bonjour le monde!", Resource1.FormatHello("le monde"));
    }

    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void StringValue()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("value", Resource1.Sample);

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
        Assert.Equal("valeur", Resource1.Sample);
    }

    [Fact]
    public void GetStringWithDefaultValue()
    {
        // Ensure the value is not nullable
        Assert.Equal(3, Resource1.GetString("UnknownValue", defaultValue: "abc").Length);
    }

    [Fact]
    public void TextFile()
    {
       Assert.Equal("test", Resource1.TextFile1);
    }

    [Fact]
    public void BinaryFile()
    {
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Resource1.Image1[..8]);
    }
}
