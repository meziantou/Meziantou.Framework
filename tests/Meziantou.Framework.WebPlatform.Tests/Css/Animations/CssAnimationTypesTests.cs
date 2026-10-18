using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class CssAnimationTypesTests
{
    [Fact]
    public void EveryLonghandHasAnAnimationType()
    {
        var missing = CssPropertyRegistry.Properties
            .Where(property => property.AliasOf is null && !property.IsShorthand)
            .Where(property => property.AnimationType is CssAnimationTypeKind.Unspecified or CssAnimationTypeKind.Special or CssAnimationTypeKind.Shorthand && !CssAnimationTypes.HasOverride(property.Name))
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("opacity", "ByComputedValue")]
    [InlineData("color", "ByComputedValue")]
    [InlineData("box-sizing", "Discrete")]
    [InlineData("background-size", "RepeatableList")]
    [InlineData("visibility", "Visibility")]
    [InlineData("display", "Display")]
    [InlineData("transform", "TransformList")]
    [InlineData("box-shadow", "ShadowList")]
    [InlineData("text-shadow", "ShadowList")]
    [InlineData("d", "Path")]
    [InlineData("animation-name", "NotAnimatable")]
    [InlineData("will-change", "NotAnimatable")]
    [InlineData("margin", "NotAnimatable")] // shorthand
    [InlineData("-webkit-transform", "TransformList")] // legacy alias
    [InlineData("unknown", "NotAnimatable")]
    public void Get(string property, string expected) => Assert.Equal(expected, CssAnimationTypes.Get(property).ToString());
}
