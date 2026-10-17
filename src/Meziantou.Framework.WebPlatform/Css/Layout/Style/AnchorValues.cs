using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of the value of <c>position-anchor</c> (https://drafts.csswg.org/css-anchor-position-1/#position-anchor).</summary>
internal enum PositionAnchorKind : byte
{
    /// <summary><c>normal</c>: <c>none</c> when <c>position-area</c> is <c>none</c>, else <c>auto</c>.</summary>
    Normal,
    None,

    /// <summary><c>auto</c>: the implicit anchor element.</summary>
    Auto,

    /// <summary>An &lt;anchor-name&gt;.</summary>
    Name,

    /// <summary><c>match-parent</c>: the default anchor element of the parent (or of the originating element).</summary>
    MatchParent,
}

/// <summary>The value of <c>position-try-order</c> (https://drafts.csswg.org/css-anchor-position-1/#position-try-order-property).</summary>
internal enum PositionTryOrder : byte
{
    Normal,
    MostWidth,
    MostHeight,
    MostBlockSize,
    MostInlineSize,
}

/// <summary>The value of <c>position-visibility</c> (https://drafts.csswg.org/css-anchor-position-1/#position-visibility).</summary>
[Flags]
internal enum PositionVisibility : byte
{
    /// <summary><c>always</c>.</summary>
    Always = 0,
    AnchorValid = 1,
    AnchorVisible = 2,
    NoOverflow = 4,
}

/// <summary>A &lt;try-tactic&gt; (https://drafts.csswg.org/css-anchor-position-1/#typedef-position-try-fallbacks-try-tactic).</summary>
internal enum TryTactic : byte
{
    FlipBlock,
    FlipInline,
    FlipStart,
    FlipX,
    FlipY,
}

/// <summary>
/// A tree-scoped name (https://drafts.csswg.org/css-scoping-1/#css-tree-scoped-name): an anchor name, a <c>@position-try</c> name, with
/// the root of the tree of the style sheet of its declaration (a <see cref="Document"/> or a <see cref="ShadowRoot"/>).
/// </summary>
internal readonly record struct TreeScopedName(string Name, Node Scope);

/// <summary>An entry of <c>position-try-fallbacks</c>: a <c>@position-try</c> rule name and try tactics, or a &lt;position-area&gt;.</summary>
internal sealed class PositionTryFallback
{
    /// <summary>The name of a <c>@position-try</c> rule, or null.</summary>
    public string? RuleName { get; init; }

    /// <summary>The try tactics, composed in order.</summary>
    public IReadOnlyList<TryTactic> Tactics { get; init; } = [];

    /// <summary>The &lt;position-area&gt; of the option (the option only sets <c>position-area</c>), or null.</summary>
    public string? PositionArea { get; init; }

    /// <summary>The serialized entry (identifies the option for the last successful position option).</summary>
    public required string Text { get; init; }
}

/// <summary>
/// The properties of the anchor positioning (https://drafts.csswg.org/css-anchor-position-1/): the anchor names, their scopes,
/// the default anchor, <c>position-area</c>, the position options and <c>position-visibility</c>.
/// </summary>
internal sealed class AnchorPositioningStyle
{
    internal AnchorPositioningStyle(ComputedStyle style)
    {
        var defaultScope = GetDefaultScope(style);
        var anchorName = style.GetComputedValue("anchor-name");
        if (anchorName != "none")
        {
            var scope = GetScope(style, "anchor-name", defaultScope);
            AnchorNames = [.. SplitList(anchorName).Select(name => new TreeScopedName(name, scope))];
        }

        var anchorScope = style.GetComputedValue("anchor-scope");
        if (anchorScope == "all")
        {
            AnchorScopeAll = true;
            AnchorScopeTree = GetScope(style, "anchor-scope", defaultScope);
        }
        else if (anchorScope != "none")
        {
            AnchorScopeTree = GetScope(style, "anchor-scope", defaultScope);
            AnchorScope = [.. SplitList(anchorScope)];
        }

        var positionAnchor = style.GetComputedValue("position-anchor");
        PositionAnchor = positionAnchor switch
        {
            "normal" => PositionAnchorKind.Normal,
            "none" => PositionAnchorKind.None,
            "auto" => PositionAnchorKind.Auto,
            "match-parent" => PositionAnchorKind.MatchParent,
            _ => PositionAnchorKind.Name,
        };

        if (PositionAnchor == PositionAnchorKind.Name)
        {
            PositionAnchorName = new TreeScopedName(positionAnchor, GetScope(style, "position-anchor", defaultScope));
        }

        var positionArea = style.GetComputedValue("position-area");
        PositionArea = positionArea == "none" ? null : PositionAreaValue.Parse(positionArea);

        var fallbacks = style.GetComputedValue("position-try-fallbacks");
        if (fallbacks != "none")
        {
            TryFallbacksScope = GetScope(style, "position-try-fallbacks", defaultScope);
            TryFallbacks = ParseFallbacks(fallbacks);
        }

        TryOrder = style.GetComputedValue("position-try-order") switch
        {
            "most-width" => PositionTryOrder.MostWidth,
            "most-height" => PositionTryOrder.MostHeight,
            "most-block-size" => PositionTryOrder.MostBlockSize,
            "most-inline-size" => PositionTryOrder.MostInlineSize,
            _ => PositionTryOrder.Normal,
        };

        var visibility = PositionVisibility.Always;
        foreach (var keyword in style.GetComputedValue("position-visibility").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            visibility |= keyword switch
            {
                "anchor-valid" or "anchors-valid" => PositionVisibility.AnchorValid,
                "anchor-visible" or "anchors-visible" => PositionVisibility.AnchorVisible,
                "no-overflow" => PositionVisibility.NoOverflow,
                _ => PositionVisibility.Always,
            };
        }

        Visibility = visibility;
        UsesAnchorFunctions = UsesAnchorFunctionsIn(style);
    }

    /// <summary>The anchor names of <c>anchor-name</c> (https://drafts.csswg.org/css-anchor-position-1/#name), or null for <c>none</c>.</summary>
    public TreeScopedName[]? AnchorNames { get; }

    /// <summary>Whether <c>anchor-scope</c> is <c>all</c>.</summary>
    public bool AnchorScopeAll { get; }

    /// <summary>The names of <c>anchor-scope</c> (https://drafts.csswg.org/css-anchor-position-1/#anchor-scope), or null.</summary>
    public string[]? AnchorScope { get; }

    /// <summary>The tree of the declaration of <c>anchor-scope</c> (its names are strictly matched).</summary>
    public Node? AnchorScopeTree { get; }

    /// <summary>Whether <c>anchor-scope</c> is not <c>none</c>.</summary>
    public bool HasAnchorScope => AnchorScopeAll || AnchorScope is not null;

    public PositionAnchorKind PositionAnchor { get; }

    /// <summary>The &lt;anchor-name&gt; of <c>position-anchor</c> (<see cref="PositionAnchorKind.Name"/>).</summary>
    public TreeScopedName? PositionAnchorName { get; }

    /// <summary>The value of <c>position-area</c>, or null for <c>none</c>.</summary>
    public PositionAreaValue? PositionArea { get; }

    /// <summary>The entries of <c>position-try-fallbacks</c>, or null for <c>none</c>.</summary>
    public IReadOnlyList<PositionTryFallback>? TryFallbacks { get; }

    /// <summary>The tree the <c>@position-try</c> names of <c>position-try-fallbacks</c> are looked up from.</summary>
    public Node? TryFallbacksScope { get; }

    public PositionTryOrder TryOrder { get; }

    public PositionVisibility Visibility { get; }

    /// <summary>Whether an inset, margin or sizing property contains an anchor function (<c>anchor()</c>, <c>anchor-size()</c>).</summary>
    public bool UsesAnchorFunctions { get; }

    /// <summary>Returns whether the computed values of the properties that accept the anchor functions contain one.</summary>
    internal static bool UsesAnchorFunctionsIn(ComputedStyle style)
    {
        foreach (var property in AnchorFunctionProperties)
        {
            if (style.GetComputedValue(property).Contains("anchor", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>The longhands that accept the anchor functions: the insets, the margins and the sizes.</summary>
    internal static readonly string[] AnchorFunctionProperties =
    [
        "top", "right", "bottom", "left", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "width", "height", "min-width", "min-height", "max-width", "max-height",
    ];

    /// <summary>The root of the tree of the element (the tree of the declarations of its style attribute).</summary>
    private static Node GetDefaultScope(ComputedStyle style) => style.Element.GetRoot() is { } root and (Document or ShadowRoot) ? root : style.Element.OwnerDocument ?? (Node)style.Element;

    /// <summary>Returns the tree of the declaration of a property (https://drafts.csswg.org/css-scoping-1/#css-tree-scoped-name).</summary>
    private static Node GetScope(ComputedStyle style, string property, Node defaultScope) => style.TryGetCascadedValue(property, out var cascaded) && cascaded.TreeScope is { } scope ? scope : defaultScope;

    private static string[] SplitList(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<PositionTryFallback> ParseFallbacks(string value)
    {
        var result = new List<PositionTryFallback>();
        foreach (var entry in SplitList(value))
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? ruleName = null;
            List<TryTactic>? tactics = null;
            var isArea = false;
            foreach (var part in parts)
            {
                if (part.StartsWith("--", StringComparison.Ordinal))
                {
                    ruleName = CssParser.ParseListOfComponentValues(part) is [CssPreservedToken { Type: CssTokenType.Ident } ident] ? ident.Token.Value : part;
                    continue;
                }

                TryTactic? tactic = part switch
                {
                    "flip-block" => TryTactic.FlipBlock,
                    "flip-inline" => TryTactic.FlipInline,
                    "flip-start" => TryTactic.FlipStart,
                    "flip-x" => TryTactic.FlipX,
                    "flip-y" => TryTactic.FlipY,
                    _ => null,
                };

                if (tactic is { } value2)
                {
                    (tactics ??= []).Add(value2);
                }
                else
                {
                    isArea = true;
                }
            }

            result.Add(isArea
                ? new PositionTryFallback { PositionArea = entry, Text = entry }
                : new PositionTryFallback { RuleName = ruleName, Tactics = tactics is null ? [] : tactics, Text = entry });
        }

        return result;
    }
}
