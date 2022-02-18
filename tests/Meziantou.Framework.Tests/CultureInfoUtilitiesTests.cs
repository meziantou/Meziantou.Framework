using System.Globalization;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public class CultureInfoUtilitiesTests
{
    [RunIfFact(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_fr()
    {
        CultureInfo.GetCultureInfo("fr-FR").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")).Should().BeTrue();
    }

    [RunIfFact(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_fr2()
    {
        CultureInfo.GetCultureInfo("fr").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")).Should().BeTrue();
    }

    [RunIfFact(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void NeutralEquals_en()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var en = CultureInfo.GetCultureInfo("en-CA");
        fr.NeutralEquals(en).Should().BeFalse();
    }

    [Fact]
    public void UseCulture()
    {
        CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
        {
            if (RunIfFactAttribute.IsGlobalizationInvariant())
            {
                12.ToString("F2", CultureInfo.CurrentCulture).Should().Be("12.00");
            }
            else
            {
                12.ToString("F2", CultureInfo.CurrentCulture).Should().Be("12,00");
            }
        });
    }
}
