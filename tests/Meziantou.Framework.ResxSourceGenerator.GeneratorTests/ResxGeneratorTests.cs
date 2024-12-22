#pragma warning disable CA1304
#pragma warning disable MA0011
using System.Globalization;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.ResxSourceGenerator.GeneratorTests;

public class ResxGeneratorTests
{
    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void FormatString()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Resource1.FormatHello("world").Should().Be("Hello world!");

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
        Resource1.FormatHello("le monde").Should().Be("Bonjour le monde!");
    }

    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void StringValue()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Resource1.Sample.Should().Be("value");

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
        Resource1.Sample.Should().Be("valeur");
    }

    [Fact]
    public void GetStringWithDefaultValue()
    {
        // Ensure the value is not nullable
        Resource1.GetString("UnknownValue", defaultValue: "abc").Should().HaveLength(3);
    }

    [Fact]
    public void TextFile()
    {
        Resource1.TextFile1.Should().Be("test");
    }

    [Fact]
    public void BinaryFile()
    {
        Resource1.Image1.Should().StartWith([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }
}
