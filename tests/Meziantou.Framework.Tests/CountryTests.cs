using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public sealed class CountryTests
{
    [Theory]
    [InlineData("fr")]
    [InlineData("FR")]
    [InlineData("fR")]
    [InlineData("Fr")]
    public void FrenchFlag(string name)
    {
        Assert.Equal("\U0001F1EB\U0001F1F7", Country.GetUnicodeFlag(name));
        Assert.Equal("\U0001F1EB\U0001F1F7", Country.GetUnicodeFlag(name));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void FrenchFlagFromRegion()
    {
        Assert.Equal("\U0001F1EB\U0001F1F7", Country.GetUnicodeFlag(new RegionInfo("FR")));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Country_GetUnicodeFlag_Example()
    {
        #region Country_GetUnicodeFlag
        var region = new RegionInfo("US");
        string flag = Country.GetUnicodeFlag(region);
        Assert.Equal("\U0001F1FA\U0001F1F8", flag); // US flag emoji

        string flag2 = Country.GetUnicodeFlag("FR");
        Assert.Equal("\U0001F1EB\U0001F1F7", flag2); // FR flag emoji
        #endregion
    }

    [Theory]
    [InlineData("")]
    [InlineData("F")]
    [InlineData("FRA")]
    [InlineData("12")]
    [InlineData("F1")]
    [InlineData("419")]
    [InlineData("@[")]
    [InlineData("`{")]
    [InlineData("\u00C9\u00C9")]
    public void InvalidRegionName(string name)
    {
        Assert.Throws<ArgumentException>("twoLetterISORegionName", () => Country.GetUnicodeFlag(name));
        Assert.False(Country.TryGetUnicodeFlag(name, out var flag));
        Assert.Null(flag);
    }

    [Fact]
    public void NullRegionName()
    {
        Assert.Throws<ArgumentNullException>("twoLetterISORegionName", () => Country.GetUnicodeFlag((string)null!));
        Assert.False(Country.TryGetUnicodeFlag((string?)null, out _));
    }

    [Fact]
    public void TryGetUnicodeFlag_NameIsNotNullWhenTrue()
    {
        var name = AsNullable("FR");

        // Fails to compile (CS8602) if the parameter is not annotated with [NotNullWhen(true)]
        Assert.True(Country.TryGetUnicodeFlag(name, out _) && name.Length is 2);
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void TryGetUnicodeFlag_RegionIsNotNullWhenTrue()
    {
        var region = AsNullable(new RegionInfo("FR"));

        // Fails to compile (CS8602) if the parameter is not annotated with [NotNullWhen(true)]
        Assert.True(Country.TryGetUnicodeFlag(region, out _) && region.TwoLetterISORegionName is "FR");
    }

    private static T? AsNullable<T>(T value) where T : class => value;

    [Fact]
    public void NullRegion()
    {
        Assert.Throws<ArgumentNullException>("region", () => Country.GetUnicodeFlag((RegionInfo)null!));
        Assert.False(Country.TryGetUnicodeFlag((RegionInfo?)null, out var flag));
        Assert.Null(flag);
    }

    [Theory]
    [InlineData("az", "\U0001F1E6\U0001F1FF")]
    [InlineData("ZA", "\U0001F1FF\U0001F1E6")]
    public void FirstAndLastLetters(string name, string expected)
    {
        Assert.Equal(expected, Country.GetUnicodeFlag(name));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void RegionWithoutLetterCode()
    {
        var region = new RegionInfo("es-419");
        Assert.Throws<ArgumentException>("region", () => Country.GetUnicodeFlag(region));
        Assert.False(Country.TryGetUnicodeFlag(region, out _));
    }

    [Fact]
    public void Country_TryGetUnicodeFlag_Example()
    {
        #region Country_TryGetUnicodeFlag
        Assert.True(Country.TryGetUnicodeFlag("FR", out var flag));
        Assert.Equal("\U0001F1EB\U0001F1F7", flag); // FR flag emoji

        // "419" (Latin America) is a numeric UN M.49 code, not a country code
        Assert.False(Country.TryGetUnicodeFlag("419", out _));
        #endregion
    }
}
