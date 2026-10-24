using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The typed view of a <see cref="ComputedStyle"/> used by the layout: the computed values (strings) are converted to
/// enums and to used lengths in CSS px, multiplied by the effective zoom (https://drafts.csswg.org/css-viewport/#zoom-property).
/// The percentages are kept, and resolved by the layout against their basis.
/// </summary>
/// <remarks>
/// The view is created on first use (<see cref="ComputedStyle.Layout"/>) and lives as long as its style: a style is
/// immutable once computed (a change creates a new style), so the view needs no invalidation. The values that are read
/// alone (display, position, white-space-collapse...) are converted one by one on first access; the values that the
/// layout reads together (the box model, the text properties, flexbox...) are converted by groups, allocated on first
/// access, so a style that is only queried for its display does not pay for the rest.
/// </remarks>
internal sealed class LayoutStyle
{
    private readonly ComputedStyle _style;
    private ResolvedFields _resolved;

    private Display _display;
    private PositionKind _position;
    private FloatKind _float;
    private ClearKind _clear;
    private MarginTrimSides _marginTrim;
    private BoxSizing _boxSizing;
    private OverflowKind _overflowX;
    private OverflowKind _overflowY;
    private WritingMode _writingMode;
    private Direction _direction;
    private TextOrientation _textOrientation;
    private AlignmentBaseline _dominantBaseline;
    private UnicodeBidi _unicodeBidi;
    private Visibility _visibility;
    private ContentVisibility _contentVisibility;
    private Containment _contain;
    private ContainerType _containerType;
    private WhiteSpaceCollapse _whiteSpaceCollapse;
    private TextWrapMode _textWrapMode;
    private TextTransform _textTransform;
    private DescendantAlignment _descendantAlignment;
    private bool _establishesFixedPositionContainingBlock;
    private bool _willChangePosition;
    private bool _containingBlockByContainmentOnly;
    private bool _hasTransform;
    private bool _isMultiColumnContainer;
    private bool _isScrollInitialTarget;
    private ContinueKind _continue;
    private TextGroupAlign _textGroupAlign;
    private double _zoom;
    private double _effectiveZoom;
    private double _fontSize;
    private LineHeight _lineHeight;

    private BoxModelStyle? _box;
    private TextLayoutStyle? _text;
    private FontLayoutStyle? _font;
    private FragmentationStyle? _fragmentation;
    private RareFields? _rare;

    /// <summary>The groups and the values that few boxes use or that rarely have a value, allocated on first use to keep the styles small.</summary>
    private sealed class RareFields
    {
        public InsetStyle? Insets;
        public FlexLayoutStyle? Flex;
        public AlignmentStyle? Alignment;
        public GridLayoutStyle? Grid;
        public TableLayoutStyle? Table;
        public GapDecorationStyle? GapDecorations;
        public OverflowStyle? Overflow;
        public ReplacedStyle? Replaced;
        public AnchorPositioningStyle? Anchor;

        /// <summary>The <see cref="WillChangeProperties"/> when there are some.</summary>
        public string[]? WillChangeProperties;
        public BlockStep? BlockStep;

        /// <summary>The <see cref="VerticalAlign"/> when it is not the initial value.</summary>
        public VerticalAlign VerticalAlign;
    }

    private RareFields Rare => _rare ??= new RareFields();

    internal LayoutStyle(ComputedStyle style) => _style = style;

    /// <summary>The computed style (for the values that have no typed accessor).</summary>
    public ComputedStyle Style => _style;

    // ----------------------------------------------------------------------------------------------------------------
    // Box generation and positioning
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-display/#the-display-properties (the computed value is blockified when needed).</summary>
    public Display Display
    {
        get
        {
            if (!IsResolved(ResolvedFields.Display))
            {
                _display = Display.Parse(_style.GetComputedValue("display"));
                _resolved |= ResolvedFields.Display;
            }

            return _display;
        }
    }

    /// <summary>https://drafts.csswg.org/css-position-3/#position-property</summary>
    public PositionKind Position
    {
        get
        {
            if (!IsResolved(ResolvedFields.Position))
            {
                _position = LayoutKeywords.ParsePosition(_style.GetComputedValue("position"));
                _resolved |= ResolvedFields.Position;
            }

            return _position;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css2/#float-position (none for an absolutely positioned box: its computed value is none). As in
    /// the browsers, a br or wbr element is not floated: it is a line break (opportunity) of its line (see <see cref="BoxTreeBuilder"/>).
    /// </summary>
    public FloatKind Float
    {
        get
        {
            if (!IsResolved(ResolvedFields.Float))
            {
                _float = _style is { PseudoElement: null, Element: var element } && (element.IsHtml(TagId.Br) || element.IsHtml(TagId.Wbr))
                    ? FloatKind.None
                    : LayoutKeywords.ParseFloat(_style.GetComputedValue("float"));
                _resolved |= ResolvedFields.Float;
            }

            return _float;
        }
    }

    /// <summary>https://drafts.csswg.org/css2/#flow-control</summary>
    public ClearKind Clear
    {
        get
        {
            if (!IsResolved(ResolvedFields.Clear))
            {
                _clear = LayoutKeywords.ParseClear(_style.GetComputedValue("clear"));
                _resolved |= ResolvedFields.Clear;
            }

            return _clear;
        }
    }

    /// <summary>https://drafts.csswg.org/css-box-4/#margin-trim: the sides whose adjacent margins of the in-flow children are trimmed.</summary>
    public MarginTrimSides MarginTrim
    {
        get
        {
            if (!IsResolved(ResolvedFields.MarginTrim))
            {
                _marginTrim = LayoutKeywords.ParseMarginTrim(_style.GetComputedValue("margin-trim"));
                _resolved |= ResolvedFields.MarginTrim;
            }

            return _marginTrim;
        }
    }

    /// <summary>Whether the box is absolutely positioned (position: absolute or fixed): it is out of flow.</summary>
    public bool IsAbsolutelyPositioned => Position is PositionKind.Absolute or PositionKind.Fixed;

    /// <summary>Whether the box is positioned (https://drafts.csswg.org/css-position-3/#positioned-box): position is not static.</summary>
    public bool IsPositioned => Position != PositionKind.Static;

    /// <summary>Whether the box is floated (https://drafts.csswg.org/css2/#floats).</summary>
    public bool IsFloating => Float != FloatKind.None;

    /// <summary>Whether the box is out of flow (https://drafts.csswg.org/css-display/#out-of-flow): floated or absolutely positioned.</summary>
    public bool IsOutOfFlow => IsAbsolutelyPositioned || IsFloating;

    /// <summary>https://drafts.csswg.org/css-box-4/#box-sizing</summary>
    public BoxSizing BoxSizing
    {
        get
        {
            if (!IsResolved(ResolvedFields.BoxSizing))
            {
                _boxSizing = _style.GetComputedValue("box-sizing") == "border-box" ? BoxSizing.BorderBox : BoxSizing.ContentBox;
                _resolved |= ResolvedFields.BoxSizing;
            }

            return _boxSizing;
        }
    }

    /// <summary>
    /// The used value of overflow-x (https://drafts.csswg.org/css-overflow-3/#overflow-control): visible becomes auto when the
    /// other axis is neither visible nor clip. As in the browsers, clip stays clip (the box is a scroll container in the other
    /// axis only: WPT position-sticky-single-axis-used-values.html), instead of the hidden of the specification.
    /// </summary>
    public OverflowKind OverflowX
    {
        get
        {
            ResolveOverflow();
            return _overflowX;
        }
    }

    /// <summary>The used value of overflow-y (see <see cref="OverflowX"/>).</summary>
    public OverflowKind OverflowY
    {
        get
        {
            ResolveOverflow();
            return _overflowY;
        }
    }

    /// <summary>
    /// Whether the box is a scroll container (https://drafts.csswg.org/css-overflow-3/#scroll-container): overflow is hidden,
    /// scroll or auto in at least one axis (before the propagation to the viewport: see <c>ScrollContainers</c>).
    /// </summary>
    public bool IsScrollContainer => OverflowX is not (OverflowKind.Visible or OverflowKind.Clip) || OverflowY is not (OverflowKind.Visible or OverflowKind.Clip);

    private void ResolveOverflow()
    {
        if (IsResolved(ResolvedFields.Overflow))
            return;

        var x = LayoutKeywords.ParseOverflow(_style.GetComputedValue("overflow-x"));
        var y = LayoutKeywords.ParseOverflow(_style.GetComputedValue("overflow-y"));

        // https://drafts.csswg.org/css-overflow-3/#overflow-control: "visible/clip values of overflow compute to auto/hidden
        // (respectively) if one of overflow-x or overflow-y is neither visible nor clip" (the computed values keep the
        // specified keywords, like the browsers: this is the used value; clip is kept, like the browsers)
        var xScrolls = x is not (OverflowKind.Visible or OverflowKind.Clip);
        var yScrolls = y is not (OverflowKind.Visible or OverflowKind.Clip);
        if (xScrolls && y == OverflowKind.Visible)
        {
            y = OverflowKind.Auto;
        }
        else if (yScrolls && x == OverflowKind.Visible)
        {
            x = OverflowKind.Auto;
        }

        _overflowX = x;
        _overflowY = y;
        _resolved |= ResolvedFields.Overflow;
    }

    /// <summary>
    /// Whether the box establishes a containing block for its fixed positioned descendants (https://drafts.csswg.org/css-position-3/#fixed-cb),
    /// and so for its absolutely positioned descendants too: a transform, a perspective, preserve-3d, a filter, layout or paint
    /// containment, or the will-change values that do the same (https://drafts.csswg.org/css-will-change/#will-change).
    /// </summary>
    public bool EstablishesFixedPositionContainingBlock
    {
        get
        {
            ResolveContainingBlockFlags();
            return _establishesFixedPositionContainingBlock;
        }
    }

    /// <summary>
    /// Whether <see cref="EstablishesFixedPositionContainingBlock"/> is only true because of the layout or paint containment
    /// of the style: it does not hold for a box that the containment does not apply to (an internal table box that is not a
    /// cell..., see <see cref="UsedContainment.EstablishesFixedPositionContainingBlock"/>).
    /// </summary>
    public bool EstablishesFixedPositionContainingBlockByContainmentOnly
    {
        get
        {
            ResolveContainingBlockFlags();
            return _containingBlockByContainmentOnly;
        }
    }

    /// <summary>
    /// Whether the box establishes an absolute positioning containing block (https://drafts.csswg.org/css-position-3/#absolute-cb):
    /// it is positioned (or is the root, or will-change contains position), or it establishes a fixed positioning containing block.
    /// </summary>
    public bool EstablishesAbsolutePositionContainingBlock
    {
        get
        {
            if (_style.IsRoot || IsPositioned)
                return true;

            ResolveContainingBlockFlags();
            return _establishesFixedPositionContainingBlock || _willChangePosition;
        }
    }

    /// <summary>
    /// Whether the box has a transform (https://drafts.csswg.org/css-transforms-2/#transform-property): <c>transform</c>,
    /// <c>translate</c>, <c>rotate</c>, <c>scale</c> or <c>offset-path</c> is not <c>none</c> (see <see cref="LayoutTransforms"/>).
    /// </summary>
    public bool HasTransform
    {
        get
        {
            if (!IsResolved(ResolvedFields.Transform))
            {
                // The transform properties are not inherited: an element without cascaded value has none
                _hasTransform = _style.MayHaveTransform && ComputeHasTransform();
                _resolved |= ResolvedFields.Transform;
            }

            return _hasTransform;
        }
    }

    private bool ComputeHasTransform()
    {
        foreach (var property in (ReadOnlySpan<string>)["transform", "translate", "rotate", "scale"])
        {
            if (_style.GetComputedValue(property) != "none")
                return true;
        }

        // https://drafts.fxtf.org/motion-1/#offset-path-property: the offset path transforms the box
        return MotionPath.HasOffsetPath(_style);
    }

    private void ResolveContainingBlockFlags()
    {
        if (IsResolved(ResolvedFields.ContainingBlockFlags))
            return;

        _establishesFixedPositionContainingBlock = ComputeEstablishesFixedPositionContainingBlock(out _willChangePosition, out _containingBlockByContainmentOnly);
        _resolved |= ResolvedFields.ContainingBlockFlags;
    }

    private bool ComputeEstablishesFixedPositionContainingBlock(out bool willChangePosition, out bool byContainmentOnly)
    {
        willChangePosition = false;
        byContainmentOnly = false;
        var result = HasTransform;
        if (!result)
        {
            // https://drafts.fxtf.org/filter-effects/#FilterProperty: a filter establishes a containing block "unless the element
            // it applies to is a document root element in the current browsing context"
            foreach (var property in _style.IsRoot ? (ReadOnlySpan<string>)["perspective"] : ["perspective", "filter", "backdrop-filter"])
            {
                if (_style.GetComputedValue(property) != "none")
                {
                    result = true;
                    break;
                }
            }
        }

        if (!result && _style.GetComputedValue("transform-style") == "preserve-3d")
        {
            result = true;
        }

        // https://drafts.csswg.org/css-transforms-2/#backface-visibility-property: an element with backface-visibility: hidden that
        // participates in a 3D rendering context (its parent has transform-style: preserve-3d) establishes a containing block and
        // a stacking context, as in the browsers
        if (!result && _style.GetComputedValue("backface-visibility") == "hidden" && _style.Parent?.GetComputedValue("transform-style") == "preserve-3d")
        {
            result = true;
        }

        foreach (var property in WillChangeProperties)
        {
            if (property is "transform" or "translate" or "rotate" or "scale" or "offset-path" or "perspective" or "transform-style" or "contain"
                || (property is "filter" or "backdrop-filter" && !_style.IsRoot))
            {
                result = true;
            }
            else if (property == "position")
            {
                willChangePosition = true;
            }
        }

        if (!result && (HasLayoutContainment || HasPaintContainment))
        {
            result = true;
            byContainmentOnly = true;
        }

        return result;
    }

    /// <summary>
    /// The properties named by <c>will-change</c> (https://drafts.csswg.org/css-will-change/#will-change), in lowercase, the
    /// legacy name aliases replaced by their property (e.g. <c>-webkit-perspective</c> is <c>perspective</c>); empty for <c>auto</c>.
    /// The properties targeted by the animations of the element that are current or in effect are added to them
    /// (https://drafts.csswg.org/web-animations-1/#side-effects-section).
    /// </summary>
    public string[] WillChangeProperties
    {
        get
        {
            if (!IsResolved(ResolvedFields.WillChange))
            {
                string[] properties;
                var value = _style.GetComputedValue("will-change");
                if (value is "auto" or "")
                {
                    properties = [];
                }
                else
                {
                    properties = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i < properties.Length; i++)
                    {
                        var name = Infra.InfraAscii.ToAsciiLowercase(properties[i]);
                        properties[i] = Values.CssPropertyRegistry.Get(name)?.Name ?? name;
                    }
                }

                if (_style.GetRelevantAnimatedProperties() is { } animated)
                {
                    animated.UnionWith(properties);
                    properties = [.. animated];
                }

                if (properties.Length > 0)
                {
                    Rare.WillChangeProperties = properties;
                }

                _resolved |= ResolvedFields.WillChange;
            }

            return _rare?.WillChangeProperties ?? [];
        }
    }

    /// <summary>
    /// Whether the box is a multi-column container (https://drafts.csswg.org/css-multicol-2/#multi-column-container): column-count,
    /// column-width or column-height is not auto.
    /// </summary>
    /// <summary>text-group-align (https://drafts.csswg.org/css-text-4/#text-group-align-property), which applies to the block containers (see <see cref="TextGroupAlignment"/>).</summary>
    public TextGroupAlign TextGroupAlign
    {
        get
        {
            if (!IsResolved(ResolvedFields.TextGroupAlign))
            {
                _textGroupAlign = LayoutKeywords.ParseTextGroupAlign(_style.GetComputedValue("text-group-align"));
                _resolved |= ResolvedFields.TextGroupAlign;
            }

            return _textGroupAlign;
        }
    }

    /// <summary>
    /// continue (https://drafts.csswg.org/css-overflow-4/#continue), as it applies to the line clamping (see <see cref="LineClamp"/>):
    /// <c>-webkit-legacy</c> is <see cref="ContinueKind.WebkitLegacy"/> on a vertical <c>-webkit-box</c>
    /// (<see cref="ComputedStyle.IsLegacyLineClampBox"/>), <see cref="ContinueKind.Auto"/> otherwise; the fragmentation values are
    /// <see cref="ContinueKind.Auto"/>.
    /// </summary>
    public ContinueKind Continue
    {
        get
        {
            if (!IsResolved(ResolvedFields.Continue))
            {
                _continue = _style.GetComputedValue("continue") switch
                {
                    "collapse" => ContinueKind.Collapse,
                    "discard" => ContinueKind.Discard,
                    "-webkit-legacy" => GetLegacyContinue(),
                    _ => ContinueKind.Auto,
                };
                _resolved |= ResolvedFields.Continue;
            }

            return _continue;
        }
    }

    /// <summary>
    /// The block-step properties (https://drafts.csswg.org/css-rhythm/#block-step-size), or null when <c>block-step-size</c> is
    /// <c>none</c>.
    /// </summary>
    public BlockStep? BlockStep
    {
        get
        {
            if (!IsResolved(ResolvedFields.BlockStep))
            {
                var size = _style.GetComputedValue("block-step-size");
                if (size != "none")
                {
                    Rare.BlockStep = new BlockStep(
                        BoxModelStyle.ParseFixedLength(size, EffectiveZoom),
                        LayoutKeywords.ParseBlockStepInsert(_style.GetComputedValue("block-step-insert")),
                        LayoutKeywords.ParseBlockStepAlign(_style.GetComputedValue("block-step-align")),
                        LayoutKeywords.ParseBlockStepRound(_style.GetComputedValue("block-step-round")));
                }

                _resolved |= ResolvedFields.BlockStep;
            }

            return _rare?.BlockStep;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-overflow-4/#valdef-continue--webkit-legacy: "only takes effect if the specified value of the
    /// display property is -webkit-box or -webkit-inline-box and the value of the -webkit-box-orient property is vertical" (the
    /// display is computed first: it tells whether the box is a vertical -webkit-box).
    /// </summary>
    private ContinueKind GetLegacyContinue()
    {
        _ = Display;
        return _style.IsLegacyLineClampBox ? ContinueKind.WebkitLegacy : ContinueKind.Auto;
    }

    public bool IsMultiColumnContainer
    {
        get
        {
            if (!IsResolved(ResolvedFields.MultiColumn))
            {
                // (the multi-column properties apply to the block containers: not to a layout API container, even when it falls
                // back to the flow layout, https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers)
                _isMultiColumnContainer = (_style.GetComputedValue("column-count") != "auto" || _style.GetComputedValue("column-width") != "auto" || _style.GetComputedValue("column-height") != "auto")
                    && !Display.IsLayoutFunction;
                _resolved |= ResolvedFields.MultiColumn;
            }

            return _isMultiColumnContainer;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Writing modes (https://drafts.csswg.org/css-writing-modes-4/)
    // ----------------------------------------------------------------------------------------------------------------

    public WritingMode WritingMode
    {
        get
        {
            if (!IsResolved(ResolvedFields.WritingMode))
            {
                _writingMode = LayoutKeywords.ParseWritingMode(_style.GetComputedValue("writing-mode"));
                _resolved |= ResolvedFields.WritingMode;
            }

            return _writingMode;
        }
    }

    /// <summary>
    /// The used direction: the computed <c>direction</c>, or <c>ltr</c> for the upright vertical text
    /// (https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-upright: "This value causes the used value of
    /// direction to be ltr, and for the purposes of bidi reordering, causes all characters to be treated as strong LTR").
    /// </summary>
    public Direction Direction
    {
        get
        {
            if (!IsResolved(ResolvedFields.Direction))
            {
                _direction = _style.GetComputedValue("direction") == "rtl" && !IsUprightVerticalText ? Direction.Rtl : Direction.Ltr;
                _resolved |= ResolvedFields.Direction;
            }

            return _direction;
        }
    }

    /// <summary>
    /// Sets the used writing mode and direction of the root element: the principal writing mode of the document
    /// (https://drafts.csswg.org/css-writing-modes-4/#principal-flow), "if the :root element has a body child element, the used
    /// value of the of writing-mode and direction properties on root element are taken from the computed writing-mode and
    /// direction of the first such child element instead of from the root element's own values". The computed values are not
    /// changed. Called by the box tree for each build (the values are the ones of the root element itself without body).
    /// </summary>
    internal void SetPrincipalWritingMode(WritingMode writingMode, Direction direction)
    {
        _writingMode = writingMode;
        _direction = direction;
        _resolved |= ResolvedFields.WritingMode | ResolvedFields.Direction;
    }

    public TextOrientation TextOrientation
    {
        get
        {
            if (!IsResolved(ResolvedFields.TextOrientation))
            {
                _textOrientation = LayoutKeywords.ParseTextOrientation(_style.GetComputedValue("text-orientation"));
                _resolved |= ResolvedFields.TextOrientation;
            }

            return _textOrientation;
        }
    }

    /// <summary>
    /// Whether the text is typeset with the vertical typographic mode (https://drafts.csswg.org/css-writing-modes-4/#typographic-mode):
    /// the <c>vertical-*</c> writing modes. The <c>sideways-*</c> modes typeset the text horizontally, in lines rotated
    /// sideways: <c>text-orientation</c> and <c>text-combine-upright</c> do not apply to them.
    /// </summary>
    public bool IsVerticalTypographicMode => WritingMode is WritingMode.VerticalRl or WritingMode.VerticalLr;

    /// <summary>Whether the text is typeset upright in vertical lines: <c>text-orientation: upright</c> in the vertical typographic mode.</summary>
    public bool IsUprightVerticalText => IsVerticalTypographicMode && TextOrientation == TextOrientation.Upright;

    public UnicodeBidi UnicodeBidi
    {
        get
        {
            if (!IsResolved(ResolvedFields.UnicodeBidi))
            {
                _unicodeBidi = LayoutKeywords.ParseUnicodeBidi(_style.GetComputedValue("unicode-bidi"));
                _resolved |= ResolvedFields.UnicodeBidi;
            }

            return _unicodeBidi;
        }
    }

    /// <summary>Whether the writing mode is horizontal (horizontal-tb): the inline axis is the horizontal axis.</summary>
    public bool IsHorizontalWritingMode => WritingMode == WritingMode.HorizontalTb;

    /// <summary>Whether the writing mode is vertical: the inline axis is the vertical axis.</summary>
    public bool IsVerticalWritingMode => WritingMode != WritingMode.HorizontalTb;

    /// <summary>
    /// Whether the dominant baseline is <c>central</c> (https://drafts.csswg.org/css-inline-3/#valdef-dominant-baseline-auto): "If
    /// this property occurs on a block or inline-level box, then the computed value is alphabetic if the writing mode is horizontal
    /// or text-orientation is sideways, central otherwise" (the <c>sideways-*</c> writing modes are sideways); the other boxes use
    /// the alphabetic baseline.
    /// </summary>
    public bool HasCentralDominantBaseline => IsVerticalTypographicMode && TextOrientation != TextOrientation.Sideways;

    /// <summary>
    /// The used dominant baseline (https://drafts.csswg.org/css-inline-3/#dominant-baseline-property), never
    /// <see cref="AlignmentBaseline.Baseline"/>: <c>auto</c> is "equivalent to alphabetic in horizontal writing modes and in
    /// vertical writing modes when text-orientation is sideways", "equivalent to central in vertical writing modes when
    /// text-orientation is mixed or upright" (see <see cref="HasCentralDominantBaseline"/>); <c>middle</c> is the x-middle
    /// baseline, "except under text-orientation: upright (where the alphabetic and x-height baselines are essentially
    /// meaningless) use the central baseline".
    /// </summary>
    public AlignmentBaseline DominantBaseline
    {
        get
        {
            if (!IsResolved(ResolvedFields.DominantBaseline))
            {
                var value = LayoutKeywords.ParseAlignmentBaseline(_style.GetComputedValue("dominant-baseline"));
                _dominantBaseline = value switch
                {
                    AlignmentBaseline.Baseline => HasCentralDominantBaseline ? AlignmentBaseline.Central : AlignmentBaseline.Alphabetic,
                    AlignmentBaseline.Middle when IsUprightVerticalText => AlignmentBaseline.Central,
                    _ => value,
                };
                _resolved |= ResolvedFields.DominantBaseline;
            }

            return _dominantBaseline;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Visibility and containment
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-display-4/#visibility</summary>
    public Visibility Visibility
    {
        get
        {
            if (!IsResolved(ResolvedFields.Visibility))
            {
                _visibility = LayoutKeywords.ParseVisibility(_style.GetComputedValue("visibility"));
                _resolved |= ResolvedFields.Visibility;
            }

            return _visibility;
        }
    }

    /// <summary>https://drafts.csswg.org/css-contain-2/#content-visibility</summary>
    public ContentVisibility ContentVisibility
    {
        get
        {
            if (!IsResolved(ResolvedFields.ContentVisibility))
            {
                _contentVisibility = LayoutKeywords.ParseContentVisibility(_style.GetComputedValue("content-visibility"));
                _resolved |= ResolvedFields.ContentVisibility;
            }

            return _contentVisibility;
        }
    }

    /// <summary>The containment types of contain (https://drafts.csswg.org/css-contain-2/#contain-property), without the ones of container-type and content-visibility.</summary>
    public Containment Contain
    {
        get
        {
            if (!IsResolved(ResolvedFields.Contain))
            {
                _contain = LayoutKeywords.ParseContain(_style.GetComputedValue("contain"));

                // https://drafts.csswg.org/css-view-transitions-2/#dom-element-startviewtransition: the root element of a scoped
                // view transition is forced "to have both layout containment and view transition scoping for the duration of
                // the transition" (the computed value of contain does not change), and it keeps the size of its old state while
                // its rendering is suppressed (see ViewTransition.FrozenFrame)
                if (_style.PseudoElement is null && _style.Element.NodeDocument.GetScopedViewTransition(_style.Element) is { } transition)
                {
                    _contain |= Containment.Layout;
                    if (transition.FrozenFrame is not null)
                    {
                        _contain |= Containment.Size;
                    }
                }

                _resolved |= ResolvedFields.Contain;
            }

            return _contain;
        }
    }

    /// <summary>https://drafts.csswg.org/css-conditional-5/#container-type</summary>
    public ContainerType ContainerType
    {
        get
        {
            if (!IsResolved(ResolvedFields.ContainerType))
            {
                _containerType = LayoutKeywords.ParseContainerType(_style.GetComputedValue("container-type"));
                _resolved |= ResolvedFields.ContainerType;
            }

            return _containerType;
        }
    }

    /// <summary>
    /// Whether scroll-initial-target is <c>nearest</c> (https://drafts.csswg.org/css-scroll-snap-2/#scroll-initial-target): the
    /// box is potentially the initial scroll target of its nearest scroll container (read for every box by each layout pass).
    /// </summary>
    public bool IsScrollInitialTarget
    {
        get
        {
            if (!IsResolved(ResolvedFields.ScrollInitialTarget))
            {
                _isScrollInitialTarget = _style.GetComputedValue("scroll-initial-target") == "nearest";
                _resolved |= ResolvedFields.ScrollInitialTarget;
            }

            return _isScrollInitialTarget;
        }
    }

    /// <summary>
    /// Whether the box has size containment (https://drafts.csswg.org/css-contain-2/#containment-size) in both axes
    /// (contain: size or strict, container-type: size), or in the inline axis when <paramref name="inlineAxis"/> is true
    /// (contain: inline-size, container-type: inline-size too).
    /// </summary>
    public bool HasSizeContainment(bool inlineAxis)
    {
        if ((Contain & Containment.Size) != Containment.None || (ContainerType & ContainerType.Size) != ContainerType.Normal)
            return true;

        return inlineAxis && ((Contain & Containment.InlineSize) != Containment.None || (ContainerType & ContainerType.InlineSize) != ContainerType.Normal);
    }

    /// <summary>
    /// Whether the box has layout containment (https://drafts.csswg.org/css-contain-2/#containment-layout): contain (layout,
    /// content, strict), or a <c>content-visibility</c> other than <c>visible</c>, which changes the used value of <c>contain</c>
    /// (https://drafts.csswg.org/css-contain-2/#content-visibility). The size container types do not apply it: they only
    /// establish an independent formatting context (https://drafts.csswg.org/css-conditional-5/#container-type,
    /// csswg-drafts#10544). The boxes it does not apply to are excluded by <see cref="UsedContainment.HasLayoutContainment"/>.
    /// </summary>
    public bool HasLayoutContainment => (Contain & Containment.Layout) != Containment.None || ContentVisibility != ContentVisibility.Visible;

    /// <summary>
    /// Whether the box has paint containment (https://drafts.csswg.org/css-contain-2/#containment-paint): contain (paint,
    /// content, strict), or a <c>content-visibility</c> other than <c>visible</c>.
    /// </summary>
    public bool HasPaintContainment => (Contain & Containment.Paint) != Containment.None || ContentVisibility != ContentVisibility.Visible;

    /// <summary>
    /// Whether the element has style containment (https://drafts.csswg.org/css-contain-2/#containment-style): contain (style,
    /// content, strict), a size or anchored container type (https://drafts.csswg.org/css-anchor-position-2/#valdef-container-type-anchored:
    /// "The anchored container-type applies style containment"), or a <c>content-visibility</c> other than <c>visible</c>.
    /// </summary>
    public bool HasStyleContainment => (Contain & Containment.Style) != Containment.None || (ContainerType & (ContainerType.Size | ContainerType.InlineSize | ContainerType.Anchored)) != ContainerType.Normal
        || ContentVisibility != ContentVisibility.Visible;

    // ----------------------------------------------------------------------------------------------------------------
    // Zoom and fonts
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The computed value of zoom (https://drafts.csswg.org/css-viewport/#zoom-property).</summary>
    public double Zoom
    {
        get
        {
            ResolveZoom();
            return _zoom;
        }
    }

    /// <summary>
    /// The effective zoom (https://drafts.csswg.org/css-viewport/#effective-zoom): the product of the zoom of the element and
    /// of its ancestors in the flat tree. The used lengths of the view are multiplied by it.
    /// </summary>
    public double EffectiveZoom
    {
        get
        {
            ResolveZoom();
            return _effectiveZoom;
        }
    }

    private void ResolveZoom()
    {
        if (IsResolved(ResolvedFields.Zoom))
            return;

        // The highlight pseudo-elements have the zoom of their originating element (zoom does not apply to them)
        if (_style.IsHighlight && _style.Parent is { } originating)
        {
            _zoom = 1;
            _effectiveZoom = originating.Layout.EffectiveZoom;
        }
        else
        {
            var value = _style.GetComputedValue("zoom");
            _zoom = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && zoom > 0 && double.IsFinite(zoom) ? zoom : 1;
            _effectiveZoom = _zoom * (_style.Parent?.Layout.EffectiveZoom ?? 1);
        }

        _resolved |= ResolvedFields.Zoom;
    }

    /// <summary>
    /// The used font size in px: the computed font size multiplied by the effective zoom, and by the scaling factor of
    /// <c>text-fit</c> for the style of a box on a scaled line (<see cref="ComputedStyle.UsedFontScale"/>).
    /// </summary>
    public double FontSize
    {
        get
        {
            if (!IsResolved(ResolvedFields.FontSize))
            {
                _fontSize = _style.FontSize * EffectiveZoom * _style.UsedFontScale;
                _resolved |= ResolvedFields.FontSize;
            }

            return _fontSize;
        }
    }

    /// <summary>
    /// The line-height (https://drafts.csswg.org/css-inline-3/#line-height-property): its computed value, except for a
    /// drop-down box with its native appearance, which ignores <c>line-height</c> as in the browsers (it is laid out with
    /// <c>normal</c>, which is its resolved value; the computed value and the <c>lh</c> unit are not changed: WPT
    /// css/css-values/lh-unit-004.html, html/rendering/replaced-elements/the-select-element/select-1-line-height.html).
    /// </summary>
    public LineHeight LineHeight
    {
        get
        {
            if (!IsResolved(ResolvedFields.LineHeight))
            {
                _lineHeight = IgnoresLineHeight(_style) ? LineHeight.Parse("normal", EffectiveZoom) : LineHeight.Parse(_style.GetComputedValue("line-height"), EffectiveZoom);
                _resolved |= ResolvedFields.LineHeight;
            }

            return _lineHeight;
        }
    }

    /// <summary>Returns whether the box of a style is laid out with <c>line-height: normal</c> whatever its line-height (see <see cref="LineHeight"/>).</summary>
    internal static bool IgnoresLineHeight(ComputedStyle style)
        => style.PseudoElement is null && style.Element is Dom.HtmlSelectElement { IsMultiple: false, DisplaySize: <= 1 } && style.GetComputedValue("appearance") is "auto" or "menulist";

    /// <summary>
    /// The used line height in px: normal is the sum of the ascent, the descent and the line gap of the first available font
    /// (https://drafts.csswg.org/css-inline-3/#valdef-line-height-normal) at the zoomed font size.
    /// </summary>
    public double UsedLineHeight => LineHeight.Kind == LineHeightKind.Normal ? _style.Fonts.PrimaryFont.GetMetrics(FontSize).NormalLineHeight : LineHeight.Resolve(FontSize, 0);

    // ----------------------------------------------------------------------------------------------------------------
    // Text (the properties read for each text run)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-text-4/#white-space-collapsing</summary>
    public WhiteSpaceCollapse WhiteSpaceCollapse
    {
        get
        {
            if (!IsResolved(ResolvedFields.WhiteSpaceCollapse))
            {
                _whiteSpaceCollapse = LayoutKeywords.ParseWhiteSpaceCollapse(_style.GetComputedValue("white-space-collapse"));
                _resolved |= ResolvedFields.WhiteSpaceCollapse;
            }

            return _whiteSpaceCollapse;
        }
    }

    /// <summary>https://drafts.csswg.org/css-text-4/#text-wrap-mode</summary>
    public TextWrapMode TextWrapMode
    {
        get
        {
            if (!IsResolved(ResolvedFields.TextWrapMode))
            {
                _textWrapMode = _style.GetComputedValue("text-wrap-mode") == "nowrap" ? TextWrapMode.NoWrap : TextWrapMode.Wrap;
                _resolved |= ResolvedFields.TextWrapMode;
            }

            return _textWrapMode;
        }
    }

    /// <summary>https://drafts.csswg.org/css-text-4/#text-transform-property</summary>
    public TextTransform TextTransform
    {
        get
        {
            if (!IsResolved(ResolvedFields.TextTransform))
            {
                _textTransform = LayoutKeywords.ParseTextTransform(_style.GetComputedValue("text-transform"));
                _resolved |= ResolvedFields.TextTransform;
            }

            return _textTransform;
        }
    }

    /// <summary>
    /// How the descendants of the element are aligned (https://html.spec.whatwg.org/multipage/rendering.html#align-descendants):
    /// the alignment of the element when it aligns its descendants (<see cref="PresentationalHints.GetDescendantAlignment"/>),
    /// otherwise the one of its nearest ancestor in the flat tree that does ("When multiple elements are to align a particular
    /// descendant, the most deeply nested such element is expected to override the others"). It is not a CSS property: it
    /// follows the inheritance chain of the styles, up to the nearest table (as in the browsers).
    /// </summary>
    public DescendantAlignment DescendantAlignment
    {
        get
        {
            if (!IsResolved(ResolvedFields.DescendantAlignment))
            {
                var alignment = DescendantAlignment.None;
                for (var style = _style; style is not null; style = style.Parent)
                {
                    var layout = style.Layout;
                    if (layout.IsResolved(ResolvedFields.DescendantAlignment))
                    {
                        alignment = layout._descendantAlignment;
                        break;
                    }

                    if (style.PseudoElement is null && PresentationalHints.GetDescendantAlignment(style.Element) is { } own)
                    {
                        alignment = own;
                        break;
                    }

                    // As in the browsers, the alignment does not cross a table: they reset the text-align of the tables that
                    // inherit their -webkit-center, -webkit-left or -webkit-right value (the one of the center elements and of
                    // the align attributes) to start
                    if (style.PseudoElement is null && style.Element is Dom.HtmlTableElement)
                        break;
                }

                _descendantAlignment = alignment;
                _resolved |= ResolvedFields.DescendantAlignment;
            }

            return _descendantAlignment;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-inline-3/#propdef-vertical-align (its longhands): read alone for the inline boxes and the
    /// atomic inlines, without the other properties of <see cref="Text"/> (and without computing them when no declaration sets them).
    /// </summary>
    public VerticalAlign VerticalAlign
    {
        get
        {
            if (!IsResolved(ResolvedFields.VerticalAlign))
            {
                // The longhands of vertical-align are not inherited: without declaration, they have their initial value (baseline)
                var verticalAlign = _text?.VerticalAlign
                    ?? (_style.MayHaveCascadedValueStartingWith("alignment-baseline", "baseline-") ? TextLayoutStyle.ParseVerticalAlign(_style, EffectiveZoom) : default);
                if (verticalAlign != default)
                {
                    Rare.VerticalAlign = verticalAlign;
                }

                _resolved |= ResolvedFields.VerticalAlign;
            }

            return _rare?.VerticalAlign ?? default;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Groups of properties
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the box may have margins, borders or paddings: false when no declaration sets them (they are not inherited), so
    /// the inline layout does not compute <see cref="Box"/> for the inline boxes without edges.
    /// </summary>
    public bool MayHaveBoxEdges => _box is not null || _style.MayHaveCascadedValueStartingWith("margin", "padding", "border");

    /// <summary>The box model: sizes, margins, paddings, borders, aspect ratio (converted together on first access).</summary>
    public BoxModelStyle Box => _box ??= Share(new BoxModelStyle(_style, EffectiveZoom));

    /// <summary>The insets and z-index of the positioned boxes.</summary>
    public InsetStyle Insets => _rare?.Insets ?? (Rare.Insets = Share(new InsetStyle(_style, EffectiveZoom)));

    /// <summary>The properties of the inline layout and of the line breaking.</summary>
    public TextLayoutStyle Text => _text ??= Share(new TextLayoutStyle(_style, EffectiveZoom, FontSize));

    /// <summary>The properties of the font matching (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm).</summary>
    public FontLayoutStyle Font => _font ??= new FontLayoutStyle(_style);

    /// <summary>The properties of the flex containers and of the flex items.</summary>
    public FlexLayoutStyle Flex => _rare?.Flex ?? (Rare.Flex = Share(new FlexLayoutStyle(_style, EffectiveZoom)));

    /// <summary>The box alignment properties and the gaps.</summary>
    public AlignmentStyle Alignment => _rare?.Alignment ?? (Rare.Alignment = Share(new AlignmentStyle(_style, EffectiveZoom)));

    /// <summary>The properties of the grid containers and of the grid items.</summary>
    public GridLayoutStyle Grid => _rare?.Grid ?? (Rare.Grid = new GridLayoutStyle(_style, EffectiveZoom));

    /// <summary>The properties of the tables.</summary>
    public TableLayoutStyle Table => _rare?.Table ?? (Rare.Table = new TableLayoutStyle(_style, EffectiveZoom));

    /// <summary>The properties of the multi-column layout and of the fragmentation.</summary>
    public FragmentationStyle Fragmentation => _fragmentation ??= Share(new FragmentationStyle(_style, EffectiveZoom));

    /// <summary>The gap decoration properties (https://drafts.csswg.org/css-gaps-1/#gap-decorations).</summary>
    public GapDecorationStyle GapDecorations => _rare?.GapDecorations ?? (Rare.GapDecorations = new GapDecorationStyle(_style, EffectiveZoom));

    /// <summary>
    /// Whether the box may paint gap decorations: the style of a rule is not <c>none</c>. The rule styles are not inherited, so they
    /// have their initial value (none) without declaration (a cheap test before <see cref="GapDecorations"/>).
    /// </summary>
    public bool HasGapRules => _rare?.GapDecorations is { } gapDecorations
        ? gapDecorations.HasRules
        : (_style.MayHaveCascadedValue("column-rule-style") || _style.MayHaveCascadedValue("row-rule-style")) && GapDecorations.HasRules;

    /// <summary>The properties of the scroll containers and of the overflow.</summary>
    public OverflowStyle Overflow => _rare?.Overflow ?? (Rare.Overflow = new OverflowStyle(_style, EffectiveZoom));

    /// <summary>The properties of the replaced elements.</summary>
    public ReplacedStyle Replaced => _rare?.Replaced ?? (Rare.Replaced = new ReplacedStyle(_style, EffectiveZoom));

    /// <summary>The properties of the anchor positioning (https://drafts.csswg.org/css-anchor-position-1/).</summary>
    public AnchorPositioningStyle Anchor => _rare?.Anchor ?? (Rare.Anchor = new AnchorPositioningStyle(_style));

    /// <summary>
    /// Whether the box may have anchor names (https://drafts.csswg.org/css-anchor-position-1/#name): <c>anchor-name</c> is not
    /// inherited, so it has its initial value (none) without declaration (a cheap test before <see cref="Anchor"/>).
    /// </summary>
    public bool MayHaveAnchorName => _rare?.Anchor is { } anchor ? anchor.AnchorNames is not null : _style.MayHaveCascadedValue("anchor-name");

    /// <summary>Returns the group of properties of another style of the engine that is equal to <paramref name="group"/>, or <paramref name="group"/> (see <see cref="LayoutStyleGroupTable"/>).</summary>
    private T Share<T>(T group)
        where T : class, IEquatable<T> => _style.Engine.LayoutStyleGroups.Share(group);

    // ----------------------------------------------------------------------------------------------------------------
    // Caches of the sizes known without layout (see BoxSizes)
    // ----------------------------------------------------------------------------------------------------------------

    // (NaN when the size is not known without layout, see ResolvedFields.ContentWidth and ContentHeight)
    private double _contentWidth;
    private double _contentHeight;

    /// <summary>Returns the size of the content box in a physical axis computed by <see cref="BoxSizes"/>, when it is cached.</summary>
    internal bool TryGetCachedContentSize(bool horizontal, out double? size)
    {
        if (!IsResolved(horizontal ? ResolvedFields.ContentWidth : ResolvedFields.ContentHeight))
        {
            size = null;
            return false;
        }

        var value = horizontal ? _contentWidth : _contentHeight;
        size = double.IsNaN(value) ? null : value;
        return true;
    }

    /// <summary>Caches the size of the content box in a physical axis computed by <see cref="BoxSizes"/> (see <see cref="TryGetCachedContentSize"/>).</summary>
    internal void SetCachedContentSize(bool horizontal, double? size)
    {
        if (horizontal)
        {
            _contentWidth = size ?? double.NaN;
            _resolved |= ResolvedFields.ContentWidth;
        }
        else
        {
            _contentHeight = size ?? double.NaN;
            _resolved |= ResolvedFields.ContentHeight;
        }
    }

    private bool IsResolved(ResolvedFields field) => (_resolved & field) == field;

    [Flags]
    private enum ResolvedFields : ulong
    {
        Display = 1 << 0,
        Position = 1 << 1,
        Float = 1 << 2,
        Clear = 1 << 3,
        BoxSizing = 1 << 4,
        Overflow = 1 << 5,
        ContainingBlockFlags = 1 << 6,
        WritingMode = 1 << 7,
        Direction = 1 << 8,
        TextOrientation = 1 << 9,
        UnicodeBidi = 1 << 10,
        Visibility = 1 << 11,
        ContentVisibility = 1 << 12,
        Contain = 1 << 13,
        ContainerType = 1 << 14,
        Zoom = 1 << 15,
        FontSize = 1 << 16,
        LineHeight = 1 << 17,
        WhiteSpaceCollapse = 1 << 18,
        TextWrapMode = 1 << 19,
        TextTransform = 1 << 20,
        MultiColumn = 1 << 21,
        VerticalAlign = 1 << 22,
        Transform = 1 << 23,
        Continue = 1 << 24,
        DominantBaseline = 1 << 25,
        DescendantAlignment = 1 << 26,
        MarginTrim = 1 << 27,
        TextGroupAlign = 1 << 28,
        BlockStep = 1 << 29,
        ScrollInitialTarget = 1 << 30,
        WillChange = 1UL << 31,
        ContentWidth = 1UL << 32,
        ContentHeight = 1UL << 33,
    }
}
