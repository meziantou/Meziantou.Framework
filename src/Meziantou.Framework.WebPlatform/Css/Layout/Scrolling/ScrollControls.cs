using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A scroll navigation control (https://drafts.csswg.org/css-overflow-5/#scroll-navigation): a pseudo-element of
/// <see cref="Element"/> (see <see cref="ScrollControls"/> for the names). <see cref="Scroller"/> is the scroll container the
/// control is associated with: the originating element of a <c>::scroll-button()</c> or of a <c>::scroll-marker-group</c>, the
/// originating element of the group of a <c>::scroll-marker</c>.
/// </summary>
internal sealed record ScrollControlReference(Element Element, string PseudoElement, Element Scroller)
{
    public bool Is(Element element, string pseudoElement) => ReferenceEquals(Element, element) && PseudoElement == pseudoElement;

    public bool IsMarker => ScrollControls.IsMarker(PseudoElement);

    public bool IsButton => ScrollControls.IsScrollButton(PseudoElement);
}

/// <summary>
/// The pseudo-elements of the scroll navigation controls of CSS Overflow 5 (https://drafts.csswg.org/css-overflow-5/#scroll-navigation):
/// <c>::scroll-marker-group</c>, <c>::scroll-marker</c> and the four <c>::scroll-button()</c> pseudo-elements, which are named
/// by their flow-relative direction (<c>scroll-button(block-start)</c>, <c>scroll-button(inline-start)</c>,
/// <c>scroll-button(inline-end)</c>, <c>scroll-button(block-end)</c>): the physical directions of the selectors select the
/// button of the writing mode of the originating element.
/// </summary>
internal static class ScrollControls
{
    public const string MarkerGroup = "scroll-marker-group";
    public const string Marker = "scroll-marker";
    public const string ButtonBlockStart = "scroll-button(block-start)";
    public const string ButtonInlineStart = "scroll-button(inline-start)";
    public const string ButtonInlineEnd = "scroll-button(inline-end)";
    public const string ButtonBlockEnd = "scroll-button(block-end)";

    /// <summary>The name of the pseudo-element of the rules of the scroll buttons (see <see cref="Cascade.CascadeRule.PseudoElement"/>).</summary>
    public const string ButtonRuleName = "scroll-button";

    /// <summary>
    /// The name of the pseudo-element of the <c>::column::scroll-marker</c> rules (https://drafts.csswg.org/css-multicol-2/#column-pseudo):
    /// the scroll marker of each column of a multi-column container is the pseudo-element <c>column::scroll-marker(index)</c>.
    /// </summary>
    public const string ColumnMarkerRuleName = "column::scroll-marker";

    /// <summary>The name of the <c>::column</c> pseudo-element, whose style applies to every column of a multi-column container.</summary>
    public const string Column = "column";

    /// <summary>Returns the name of a column of a multi-column container, the <c>::column</c> pseudo-element of a snap area (see <see cref="SnapTarget"/>).</summary>
    public static string GetColumn(int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Column}({index})");

    /// <summary>Returns the name of the scroll marker of a column of a multi-column container.</summary>
    public static string GetColumnMarker(int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ColumnMarkerRuleName}({index})");

    /// <summary>Whether a pseudo-element is a <c>::scroll-marker</c> (of an element, or of a column).</summary>
    public static bool IsMarker(string? pseudoElement) => pseudoElement == Marker || IsColumnMarker(pseudoElement);

    public static bool IsColumnMarker(string? pseudoElement) => pseudoElement is not null && pseudoElement.StartsWith(ColumnMarkerRuleName, StringComparison.Ordinal);

    /// <summary>Returns the index of the column of a <c>::column::scroll-marker</c> (see <see cref="GetColumnMarker"/>).</summary>
    public static int GetColumnIndex(string pseudoElement)
        => int.Parse(pseudoElement.AsSpan(ColumnMarkerRuleName.Length + 1, pseudoElement.Length - ColumnMarkerRuleName.Length - 2), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The four scroll buttons, in the order they exist in (https://drafts.csswg.org/css-overflow-5/#scroll-buttons: "in order,
    /// block-start, inline-start, inline-end, and block-end").
    /// </summary>
    public static ReadOnlySpan<string> Buttons => new[] { ButtonBlockStart, ButtonInlineStart, ButtonInlineEnd, ButtonBlockEnd };

    /// <summary>Whether a pseudo-element is a scroll navigation control: the pseudo-classes written after it match its own state.</summary>
    public static bool IsScrollControl(string? pseudoElement)
        => pseudoElement is not null && (pseudoElement is Marker or MarkerGroup || IsScrollButton(pseudoElement) || IsColumnMarker(pseudoElement));

    public static bool IsScrollButton(string? pseudoElement) => pseudoElement is not null && pseudoElement.StartsWith("scroll-button", StringComparison.Ordinal);

    /// <summary>
    /// Returns the name of the scroll button selected by an argument of <c>::scroll-button()</c> on an element with the given
    /// writing mode (https://drafts.csswg.org/css-overflow-5/#typedef-scroll-button-direction): the flow-relative directions
    /// select their button, the physical directions the button of the corresponding flow-relative direction. The arguments are
    /// ASCII case-insensitive. Returns null for <c>*</c> (which selects the four buttons), for <c>prev</c> and <c>next</c> (which
    /// are not supported) and for the invalid arguments.
    /// </summary>
    public static string? GetButton(string? argument, WritingDirection writingDirection)
    {
        if (argument is null)
            return null;

        switch (InfraAscii.ToAsciiLowercase(argument))
        {
            case "block-start":
                return ButtonBlockStart;
            case "inline-start":
                return ButtonInlineStart;
            case "inline-end":
                return ButtonInlineEnd;
            case "block-end":
                return ButtonBlockEnd;
            case "up":
                return GetButton(PhysicalSide.Top, writingDirection);
            case "right":
                return GetButton(PhysicalSide.Right, writingDirection);
            case "down":
                return GetButton(PhysicalSide.Bottom, writingDirection);
            case "left":
                return GetButton(PhysicalSide.Left, writingDirection);
            default:
                return null;
        }
    }

    /// <summary>Returns the scroll button that scrolls towards a physical side in a writing mode.</summary>
    public static string GetButton(PhysicalSide side, WritingDirection writingDirection)
    {
        if (side == writingDirection.BlockStart)
            return ButtonBlockStart;

        if (side == writingDirection.InlineStart)
            return ButtonInlineStart;

        return side == writingDirection.InlineEnd ? ButtonInlineEnd : ButtonBlockEnd;
    }

    /// <summary>Returns the physical side a scroll button scrolls towards in a writing mode.</summary>
    public static PhysicalSide GetDirection(string button, WritingDirection writingDirection) => button switch
    {
        ButtonBlockStart => writingDirection.BlockStart,
        ButtonInlineStart => writingDirection.InlineStart,
        ButtonInlineEnd => writingDirection.InlineEnd,
        _ => writingDirection.BlockEnd,
    };

    /// <summary>
    /// Returns the name of the pseudo-element designated by a pseudo-element selector on an element (as parsed by
    /// <see cref="Cascade.ComputedStyleDeclarations.ParsePseudoElement"/>): <c>scroll-button(left)</c> is the scroll button of a
    /// flow-relative direction. Returns null when the argument of <c>::scroll-button()</c> does not designate one button.
    /// </summary>
    public static string? Canonicalize(string pseudoElement, Func<WritingDirection> getWritingDirection)
    {
        if (!IsScrollButton(pseudoElement))
            return pseudoElement;

        var start = pseudoElement.IndexOf('(', StringComparison.Ordinal);
        if (start < 0 || !pseudoElement.EndsWith(')', StringComparison.Ordinal))
            return null;

        var argument = InfraAscii.ToAsciiLowercase(pseudoElement[(start + 1)..^1]);
        return argument is "up" or "right" or "down" or "left" ? GetButton(argument, getWritingDirection()) : GetButton(argument, default);
    }

    /// <summary>
    /// Matches a pseudo-class written after a scroll navigation pseudo-element against the state of the pseudo-element
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-focus-pseudos: "none of the focus related pseudo-classes [...] match on the
    /// scrolling container. Instead, :focus and, when relevant :focus-visible, match on the focused pseudo-element"): the user
    /// action pseudo-classes, <c>:target-current</c>, <c>:target-before</c> and <c>:target-after</c> of a scroll marker
    /// (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker), <c>:enabled</c> and <c>:disabled</c> of a scroll button
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-buttons). The other pseudo-classes do not match.
    /// </summary>
    public static bool MatchesState(PseudoClass pseudoClass, Element element, string pseudoElement)
    {
        var document = element.NodeDocument;
        switch (pseudoClass)
        {
            case PseudoClass.Hover:
                return IsOrContains(document.DesignatedScrollControl, element, pseudoElement);

            case PseudoClass.Active:
                return IsOrContains(document.ActivelyPointedScrollControl, element, pseudoElement);

            case PseudoClass.Focus:
                return document.FocusedScrollControl is { } focused && focused.Is(element, pseudoElement);

            case PseudoClass.FocusVisible:
                return document.FocusedScrollControl is { } focusVisible && focusVisible.Is(element, pseudoElement) && document.FocusVisible;

            case PseudoClass.FocusWithin:
                // "When a ::scroll-marker is focused, :focus-within additionally matches on the ::scroll-marker-group it belongs to"
                return IsOrContains(document.FocusedScrollControl, element, pseudoElement);

            case PseudoClass.TargetCurrent or PseudoClass.TargetBefore or PseudoClass.TargetAfter:
                return IsMarker(pseudoElement) && document.LayoutEngineOrNull is { } engine && engine.MatchesScrollMarkerState(pseudoClass, element, pseudoElement);

            case PseudoClass.Enabled:
                return IsScrollButton(pseudoElement) && document.LayoutEngineOrNull is { } enabledEngine && enabledEngine.IsScrollButtonEnabled(element, pseudoElement);

            case PseudoClass.Disabled:
                return IsScrollButton(pseudoElement) && !(document.LayoutEngineOrNull is { } disabledEngine && disabledEngine.IsScrollButtonEnabled(element, pseudoElement));

            default:
                return false;
        }

        // The control, or the group of the scroll marker
        static bool IsOrContains(ScrollControlReference? control, Element element, string pseudoElement)
            => control is not null && (control.Is(element, pseudoElement) || (pseudoElement == MarkerGroup && control.IsMarker && ReferenceEquals(control.Scroller, element)));
    }
}
