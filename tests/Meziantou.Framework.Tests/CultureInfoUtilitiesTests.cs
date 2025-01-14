using System.Globalization;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public class CultureInfoUtilitiesTests
{
    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_fr()
    {
        Assert.True(CultureInfo.GetCultureInfo("fr-FR").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_fr2()
    {
        Assert.True(CultureInfo.GetCultureInfo("fr").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_en()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var en = CultureInfo.GetCultureInfo("en-CA");
        Assert.False(fr.NeutralEquals(en));
    }

    [Fact]
    public void UseCulture()
    {
        CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
        {
            if (RunIfAttribute.IsGlobalizationInvariant())
            {
                Assert.Equal("12.00", 12.ToString("F2", CultureInfo.CurrentCulture));
            }
            else
            {
                Assert.Equal("12,00", 12.ToString("F2", CultureInfo.CurrentCulture));
            }
        });
    }
}
