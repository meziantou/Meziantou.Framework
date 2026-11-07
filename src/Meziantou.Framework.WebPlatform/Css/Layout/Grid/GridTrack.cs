namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kinds of the min track sizing function of a track (https://drafts.csswg.org/css-grid-2/#min-track-sizing-function).</summary>
internal enum GridMinFunction : byte
{
    /// <summary>A fixed sizing function: a length, or a percentage of a definite size.</summary>
    Fixed,
    MinContent,
    MaxContent,
    Auto,
}

/// <summary>The kinds of the max track sizing function of a track (https://drafts.csswg.org/css-grid-2/#max-track-sizing-function).</summary>
internal enum GridMaxFunction : byte
{
    /// <summary>A fixed sizing function: a length, or a percentage of a definite size.</summary>
    Fixed,
    MinContent,
    MaxContent,

    /// <summary><c>auto</c>: treated as <c>max-content</c>, except for the stretching of the <c>auto</c> tracks.</summary>
    Auto,

    /// <summary><c>fit-content()</c>: treated as <c>max-content</c>, its growth limit clamped by its argument.</summary>
    FitContent,

    /// <summary>A &lt;flex&gt; (fr).</summary>
    Flex,
}

/// <summary>
/// A track of the grid in one axis and the state of the track sizing algorithm (https://drafts.csswg.org/css-grid-2/#algo-track-sizing):
/// its sizing functions resolved for the current sizing (the percentages of an indefinite size behave as <c>auto</c>), its
/// base size and growth limit, and its final position once the tracks are aligned.
/// </summary>
internal sealed class GridTrack
{
    /// <summary>The track sizing function of the track.</summary>
    public GridTrackSize Size { get; set; }

    /// <summary>Whether the track is an empty repetition of <c>auto-fit</c> (https://drafts.csswg.org/css-grid-2/#collapsed-track): its size is 0 and its gutters collapse.</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>Whether the track is in the explicit grid.</summary>
    public bool IsExplicit { get; set; }

    public GridMinFunction Min { get; private set; }

    public GridMaxFunction Max { get; private set; }

    /// <summary>The fixed min track sizing function, in px.</summary>
    public double MinValue { get; private set; }

    /// <summary>The fixed max track sizing function (in px), the flex factor of a flexible track, or the argument of <c>fit-content()</c> (infinity when it is a percentage of an indefinite size).</summary>
    public double MaxValue { get; private set; }

    /// <summary>https://drafts.csswg.org/css-grid-2/#base-size</summary>
    public double BaseSize { get; set; }

    /// <summary>https://drafts.csswg.org/css-grid-2/#growth-limit (infinity when it is not known yet).</summary>
    public double GrowthLimit { get; set; }

    /// <summary>https://drafts.csswg.org/css-grid-2/#infinitely-growable</summary>
    public bool InfinitelyGrowable { get; set; }

    /// <summary>The planned increase of the distribution of extra space (https://drafts.csswg.org/css-grid-2/#extra-space).</summary>
    public double PlannedIncrease { get; set; }

    /// <summary>The item-incurred increase of the distribution of extra space for the current item.</summary>
    public double ItemIncurredIncrease { get; set; }

    /// <summary>Whether the track is treated as inflexible by the current computation of the size of an fr (https://drafts.csswg.org/css-grid-2/#algo-find-fr-size).</summary>
    public bool IsTreatedAsInflexible { get; set; }

    /// <summary>The position of the start edge of the track, from the start edge of the content box of the grid container, once the tracks are aligned.</summary>
    public double Position { get; set; }

    /// <summary>Whether a gutter precedes the track (there is no gutter before the first track, and the gutters of the collapsed tracks collapse).</summary>
    public bool HasGutterBefore { get; set; }

    public bool IsFlexible => Max == GridMaxFunction.Flex;

    /// <summary>The flex factor of a flexible track.</summary>
    public double FlexFactor => Max == GridMaxFunction.Flex ? MaxValue : 0;

    public bool HasIntrinsicMin => Min != GridMinFunction.Fixed;

    /// <summary>Whether the max track sizing function is intrinsic (<c>min-content</c>, <c>max-content</c>, <c>auto</c>, <c>fit-content()</c>).</summary>
    public bool HasIntrinsicMax => Max is GridMaxFunction.MinContent or GridMaxFunction.MaxContent or GridMaxFunction.Auto or GridMaxFunction.FitContent;

    /// <summary>Whether the max track sizing function is treated as <c>max-content</c> (<c>max-content</c>, <c>auto</c>, <c>fit-content()</c>).</summary>
    public bool HasMaxContentMax => Max is GridMaxFunction.MaxContent or GridMaxFunction.Auto or GridMaxFunction.FitContent;

    /// <summary>The limit of the growth of the base size and of the growth limit of a <c>fit-content()</c> track, else infinity.</summary>
    public double FitContentLimit => Max == GridMaxFunction.FitContent ? MaxValue : double.PositiveInfinity;

    /// <summary>The used size of the track (its base size once the algorithm is complete).</summary>
    public double UsedSize => BaseSize;

    /// <summary>The position of the end edge of the track.</summary>
    public double End => Position + BaseSize;

    /// <summary>
    /// Resolves the sizing functions of the track (https://drafts.csswg.org/css-grid-2/#algo-terms): the percentages resolve
    /// against <paramref name="percentageBasis"/>, and behave as <c>auto</c> when it is null (an indefinite size, or the
    /// intrinsic sizing of the grid container); a collapsed track has fixed sizing functions of 0.
    /// </summary>
    public void ResolveFunctions(double? percentageBasis)
    {
        if (IsCollapsed)
        {
            Min = GridMinFunction.Fixed;
            Max = GridMaxFunction.Fixed;
            MinValue = 0;
            MaxValue = 0;
            return;
        }

        var size = Size;
        switch (size.Min.Kind)
        {
            case GridBreadthKind.LengthPercentage when size.Min.Length.TryResolve(percentageBasis) is { } min:
                Min = GridMinFunction.Fixed;
                MinValue = Math.Max(0, min);
                break;
            case GridBreadthKind.MinContent:
                Min = GridMinFunction.MinContent;
                break;
            case GridBreadthKind.MaxContent:
                Min = GridMinFunction.MaxContent;
                break;
            default:
                Min = GridMinFunction.Auto;
                break;
        }

        if (size.IsFitContent)
        {
            Max = GridMaxFunction.FitContent;
            MaxValue = size.Max.Length.TryResolve(percentageBasis) is { } limit ? Math.Max(0, limit) : double.PositiveInfinity;
            return;
        }

        switch (size.Max.Kind)
        {
            case GridBreadthKind.LengthPercentage when size.Max.Length.TryResolve(percentageBasis) is { } max:
                Max = GridMaxFunction.Fixed;
                MaxValue = Math.Max(0, max);
                break;
            case GridBreadthKind.Flex:
                Max = GridMaxFunction.Flex;
                MaxValue = Math.Max(0, size.Max.Flex);
                break;
            case GridBreadthKind.MinContent:
                Max = GridMaxFunction.MinContent;
                break;
            case GridBreadthKind.MaxContent:
                Max = GridMaxFunction.MaxContent;
                break;
            default:
                Max = GridMaxFunction.Auto;
                break;
        }
    }
}
