using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class CultureInfoUtilitiesTests
{
    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void NeutralEquals_fr()
    {
        Assert.True(CultureInfo.GetCultureInfo("fr-FR").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void NeutralEquals_fr2()
    {
        Assert.True(CultureInfo.GetCultureInfo("fr").NeutralEquals(CultureInfo.GetCultureInfo("fr-CA")));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void NeutralEquals_en()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var en = CultureInfo.GetCultureInfo("en-CA");
        Assert.False(fr.NeutralEquals(en));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("2147483647")]
    [InlineData("not a culture")]
    public void GetCulture_Unknown_ReturnsNull(string name)
    {
        Assert.Null(CultureInfoUtilities.GetCulture(name));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void GetCulture_Lcid()
    {
        Assert.Equal("fr-FR", CultureInfoUtilities.GetCulture("1036")?.Name);
    }

    [Fact]
    public void UseCulture()
    {
        CultureInfoUtilities.UseCulture(CultureInfo.GetCultureInfo("fr-FR"), () =>
        {
            if (TestEnvironment.IsGlobalizationInvariant())
            {
                Assert.Equal("12.00", 12.ToString("F2", CultureInfo.CurrentCulture));
            }
            else
            {
                Assert.Equal("12,00", 12.ToString("F2", CultureInfo.CurrentCulture));
            }
        });
    }

    [Fact]
    public void Documentation_ToStringInvariant()
    {
        #region FormattableExtensions_ToStringInvariant
        // Numbers, dates and any IFormattable, formatted with the invariant culture
        Assert.Equal("1234.5", 1234.5.ToStringInvariant());
        Assert.Equal("1,234.50", 1234.5m.ToStringInvariant("N2"));
        Assert.Equal("2024-03-15", new DateOnly(2024, 3, 15).ToStringInvariant("yyyy-MM-dd"));
        Assert.Equal("00:30:00", TimeSpan.FromMinutes(30).ToStringInvariant());
        #endregion
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Documentation_UseCulture()
    {
        #region CultureInfoUtilities_UseCulture
        // Run code with another current culture, then restore the previous one
        var formatted = CultureInfoUtilities.UseCulture("fr-FR", () => 1234.5.ToString("N1", CultureInfo.CurrentCulture));
        Assert.Equal("1\u202f234,5", formatted);

        // Compare cultures by language
        var canadianFrench = CultureInfo.GetCultureInfo("fr-CA");
        Assert.Equal("fr", canadianFrench.GetNeutralCulture().Name);
        Assert.True(canadianFrench.NeutralEquals(CultureInfo.GetCultureInfo("fr-FR")));
        #endregion
    }

    [Fact]
    public void Documentation_Ordinals()
    {
        #region NumberExtensions_Ordinals
        Assert.Equal("1st", NumberExtensions.ToEnglishOrdinal(1, CultureInfo.InvariantCulture));
        Assert.Equal("22nd", NumberExtensions.ToEnglishOrdinal(22, CultureInfo.InvariantCulture));
        Assert.Equal("13th", NumberExtensions.ToEnglishOrdinal(13, CultureInfo.InvariantCulture));

        Assert.Equal("1er", NumberExtensions.ToFrenchOrdinal(1, CultureInfo.InvariantCulture));
        Assert.Equal("2e", NumberExtensions.ToFrenchOrdinal(2, CultureInfo.InvariantCulture));
        #endregion
    }
}
