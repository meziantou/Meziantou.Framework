using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The tracks of the position-area grid selected in a physical axis (https://drafts.csswg.org/css-anchor-position-1/#position-area-grid):
/// the indexes of the first and last tracks, from the left (or top) track 0 to the right (or bottom) track 2; the track 1 is
/// the default anchor box.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PositionAreaTracks(int First, int Last)
{
    /// <summary>All the tracks (<c>span-all</c>).</summary>
    public static PositionAreaTracks All => new(0, 2);

    /// <summary>Whether only the center track is selected.</summary>
    public bool IsCenter => First == 1 && Last == 1;

    /// <summary>Whether the three tracks are selected.</summary>
    public bool IsAll => First == 0 && Last == 2;

    /// <summary>Whether the tracks are the <c>any</c> keyword of a &lt;position-area-query&gt;, which matches any tracks.</summary>
    public bool IsAny => First < 0;

    /// <summary>The <c>any</c> keyword of a &lt;position-area-query&gt; (https://drafts.csswg.org/css-anchor-position-2/#valdef-position-area-query-any).</summary>
    public static PositionAreaTracks Any => new(-1, -1);
}

/// <summary>
/// A computed &lt;position-area&gt; (https://drafts.csswg.org/css-anchor-position-1/#typedef-position-area): one or two keywords,
/// resolved against the writing modes of the containing block and of the box into the tracks of the position-area grid.
/// </summary>
internal sealed class PositionAreaValue
{
    private PositionAreaValue(string first, string? second)
    {
        First = first;
        Second = second;
    }

    public string First { get; }

    /// <summary>The second keyword, or null when a single keyword is given.</summary>
    public string? Second { get; }

    public static PositionAreaValue Parse(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new PositionAreaValue(parts[0], parts.Length > 1 ? parts[1] : null);
    }

    /// <summary>
    /// Resolves the value into the tracks selected in the horizontal and the vertical axes
    /// (https://drafts.csswg.org/css-anchor-position-1/#position-area-syntax).
    /// </summary>
    /// <param name="containingBlock">The writing mode and the direction of the containing block (the logical keywords).</param>
    /// <param name="self">The writing mode and the direction of the box (the self-* keywords).</param>
    public (PositionAreaTracks Horizontal, PositionAreaTracks Vertical) Resolve(WritingDirection containingBlock, WritingDirection self)
    {
        // "If only a single keyword is given, it behaves as if the second keyword is span-all if the given keyword is unambiguous
        // about its axis; otherwise, it behaves as if the given keyword was repeated"
        var second = Second ?? (GetAxis(First) is KeywordAxis.Ambiguous or KeywordAxis.SelfAmbiguous or KeywordAxis.Neutral ? First : "span-all");
        return Resolve(First, second, containingBlock, self);
    }

    /// <summary>
    /// Resolves the keywords of a &lt;position-area-query&gt; (https://drafts.csswg.org/css-anchor-position-2/#position-area-query-syntax)
    /// into the tracks selected in the horizontal and the vertical axes: <c>any</c> (<see cref="PositionAreaTracks.IsAny"/>) is an
    /// ambiguous keyword, and "if only a single keyword is given, &lt;position-area-query&gt; behaves as if the second keyword is any
    /// if the given keyword is unambiguous about its axis".
    /// </summary>
    public static (PositionAreaTracks Horizontal, PositionAreaTracks Vertical) ResolveQuery(IReadOnlyList<string> keywords, WritingDirection containingBlock, WritingDirection self)
    {
        var first = keywords[0];
        var second = keywords.Count > 1 ? keywords[1] : GetAxis(first) is KeywordAxis.Ambiguous or KeywordAxis.SelfAmbiguous or KeywordAxis.Neutral ? first : "any";
        return Resolve(first, second, containingBlock, self);
    }

    private static (PositionAreaTracks Horizontal, PositionAreaTracks Vertical) Resolve(string first, string second, WritingDirection containingBlock, WritingDirection self)
    {
        var firstAxis = GetAxis(first);
        var secondAxis = GetAxis(second);

        // "If the other keyword is unambiguous about its axis, then the ambiguous keyword is referring to the opposite axis. If
        // both keywords are ambiguous, however, then the first refers to the block axis of the box's containing block, and the
        // second to the inline axis" (the self-* keywords: of the box)
        var firstHorizontal = GetHorizontal(firstAxis, containingBlock, self);
        var secondHorizontal = GetHorizontal(secondAxis, containingBlock, self);
        if (firstHorizontal is null && secondHorizontal is null)
        {
            var writingDirection = firstAxis == KeywordAxis.SelfAmbiguous || secondAxis == KeywordAxis.SelfAmbiguous ? self : containingBlock;
            firstHorizontal = !writingDirection.IsHorizontal;
            secondHorizontal = writingDirection.IsHorizontal;
        }
        else
        {
            firstHorizontal ??= !secondHorizontal!.Value;
            secondHorizontal ??= !firstHorizontal.Value;
        }

        var firstTracks = GetTracks(first, firstAxis, firstHorizontal.Value, containingBlock, self);
        var secondTracks = GetTracks(second, secondAxis, secondHorizontal.Value, containingBlock, self);
        return firstHorizontal.Value ? (firstTracks, secondTracks) : (secondTracks, firstTracks);
    }

    private enum KeywordAxis
    {
        /// <summary><c>center</c>, <c>span-all</c>.</summary>
        Neutral,
        Horizontal,
        Vertical,
        Block,
        Inline,
        SelfBlock,
        SelfInline,

        /// <summary><c>start</c>, <c>end</c>, <c>span-start</c>, <c>span-end</c>.</summary>
        Ambiguous,

        /// <summary><c>self-start</c>, <c>self-end</c>, <c>span-self-start</c>, <c>span-self-end</c>.</summary>
        SelfAmbiguous,
    }

    private static KeywordAxis GetAxis(string keyword)
    {
        if (keyword is "center" or "span-all" or "any")
            return KeywordAxis.Neutral;

        var name = keyword.StartsWith("span-", StringComparison.Ordinal) ? keyword[5..] : keyword;
        return name switch
        {
            "left" or "right" => KeywordAxis.Horizontal,
            "top" or "bottom" => KeywordAxis.Vertical,
            "start" or "end" => KeywordAxis.Ambiguous,
            "self-start" or "self-end" => KeywordAxis.SelfAmbiguous,
            _ when name.StartsWith("x-", StringComparison.Ordinal) || name.StartsWith("self-x-", StringComparison.Ordinal) => KeywordAxis.Horizontal,
            _ when name.StartsWith("y-", StringComparison.Ordinal) || name.StartsWith("self-y-", StringComparison.Ordinal) => KeywordAxis.Vertical,
            _ when name.StartsWith("block-", StringComparison.Ordinal) => KeywordAxis.Block,
            _ when name.StartsWith("inline-", StringComparison.Ordinal) => KeywordAxis.Inline,
            _ when name.StartsWith("self-block-", StringComparison.Ordinal) => KeywordAxis.SelfBlock,
            _ => KeywordAxis.SelfInline,
        };
    }

    /// <summary>Returns whether a keyword refers to the horizontal axis, or null when its axis is ambiguous.</summary>
    private static bool? GetHorizontal(KeywordAxis axis, WritingDirection containingBlock, WritingDirection self) => axis switch
    {
        KeywordAxis.Horizontal => true,
        KeywordAxis.Vertical => false,
        KeywordAxis.Block => !containingBlock.IsHorizontal,
        KeywordAxis.Inline => containingBlock.IsHorizontal,
        KeywordAxis.SelfBlock => !self.IsHorizontal,
        KeywordAxis.SelfInline => self.IsHorizontal,
        _ => null,
    };

    private static PositionAreaTracks GetTracks(string keyword, KeywordAxis axis, bool horizontal, WritingDirection containingBlock, WritingDirection self)
    {
        if (keyword == "center")
            return new PositionAreaTracks(1, 1);

        if (keyword == "span-all")
            return PositionAreaTracks.All;

        if (keyword == "any")
            return PositionAreaTracks.Any;

        var span = keyword.StartsWith("span-", StringComparison.Ordinal);
        var name = span ? keyword[5..] : keyword;
        var isEnd = name.EndsWith("end", StringComparison.Ordinal) || name is "right" or "bottom";

        // The physical side of the "start" of the keyword: left or top for the physical keywords, else the start side of the
        // axis in the relevant writing mode
        PhysicalSide startSide;
        switch (axis)
        {
            case KeywordAxis.Horizontal when name is "left" or "right":
                startSide = PhysicalSide.Left;
                break;
            case KeywordAxis.Vertical when name is "top" or "bottom":
                startSide = PhysicalSide.Top;
                break;
            case KeywordAxis.Horizontal or KeywordAxis.Vertical:
            {
                // x-start, self-y-end...: the start of the physical axis in the writing mode of the containing block or of the box
                var writingDirection = name.StartsWith("self-", StringComparison.Ordinal) ? self : containingBlock;
                startSide = GetStartSide(writingDirection, horizontal);
                break;
            }

            case KeywordAxis.Block or KeywordAxis.Inline or KeywordAxis.Ambiguous:
                startSide = GetStartSide(containingBlock, horizontal);
                break;
            default:
                startSide = GetStartSide(self, horizontal);
                break;
        }

        var towardsStart = !isEnd;
        var physicalLow = startSide is PhysicalSide.Left or PhysicalSide.Top ? towardsStart : !towardsStart;

        // The low track (0) is on the left or the top
        return (span, physicalLow) switch
        {
            (false, true) => new PositionAreaTracks(0, 0),
            (false, false) => new PositionAreaTracks(2, 2),
            (true, true) => new PositionAreaTracks(0, 1),
            _ => new PositionAreaTracks(1, 2),
        };
    }

    /// <summary>Returns the start side of a physical axis in a writing mode (its inline start or block start side).</summary>
    private static PhysicalSide GetStartSide(WritingDirection writingDirection, bool horizontal)
        => writingDirection.IsHorizontal == horizontal ? writingDirection.InlineStart : writingDirection.BlockStart;
}
