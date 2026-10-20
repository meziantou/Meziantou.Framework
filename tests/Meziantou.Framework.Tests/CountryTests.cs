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
}
