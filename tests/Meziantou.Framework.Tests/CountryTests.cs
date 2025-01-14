using System.Globalization;
using FluentAssertions;
using TestUtilities;
using Xunit;

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

    [Fact, RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void FrenchFlagFromRegion()
    {
        Assert.Equal("\U0001F1EB\U0001F1F7", Country.GetUnicodeFlag(new RegionInfo("FR")));
    }
}
