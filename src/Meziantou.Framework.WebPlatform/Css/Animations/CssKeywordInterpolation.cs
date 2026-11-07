namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>The interpolation of the keywords whose discrete interpolation favors a value inside of the interval.</summary>
internal static class CssKeywordInterpolation
{
    /// <summary>
    /// https://drafts.csswg.org/web-animations-1/#animating-visibility: when one of the values is visible, the values of p
    /// between 0 and 1 map to visible and the other values to the closer endpoint; otherwise the interpolation is discrete.
    /// </summary>
    public static Func<double, string?>? PrepareVisibility(string from, string to)
    {
        if (from != "visible" && to != "visible")
            return null;

        return progress => progress <= 0 ? from : progress >= 1 ? to : "visible";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-display-4/#display-animation and https://drafts.csswg.org/css-contain-2/#content-visibility-animation:
    /// when one of the values is <paramref name="keyword"/> (none or hidden), the values of p between 0 and 1 map to the other
    /// value and the other values to the closer endpoint; otherwise the interpolation is discrete.
    /// </summary>
    public static Func<double, string?>? PrepareDisplay(string from, string to, string keyword)
    {
        if (from == keyword)
            return progress => progress <= 0 ? from : to;

        if (to == keyword)
            return progress => progress >= 1 ? to : from;

        return null;
    }
}
