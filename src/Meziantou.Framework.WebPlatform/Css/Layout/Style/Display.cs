using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>https://drafts.csswg.org/css-display/#typedef-display-box</summary>
internal enum DisplayBox : byte
{
    /// <summary>The element generates boxes (the other display types apply).</summary>
    Normal,

    /// <summary><c>display: contents</c>: the element does not generate a box, its children do.</summary>
    Contents,

    /// <summary><c>display: none</c>: the element and its descendants do not generate boxes.</summary>
    None,
}

/// <summary>https://drafts.csswg.org/css-display/#outer-role</summary>
internal enum DisplayOutside : byte
{
    Block,
    Inline,
    RunIn,
}

/// <summary>
/// https://drafts.csswg.org/css-display/#inner-model, with the inner display types of MathML Core, of the grid lanes
/// (CSS Grid 3) and of the legacy <c>-webkit-box</c> (https://compat.spec.whatwg.org/#display-property-mappings).
/// </summary>
internal enum DisplayInside : byte
{
    Flow,
    FlowRoot,
    Table,
    Flex,
    Grid,
    GridLanes,
    Ruby,
    Math,
    WebkitBox,
}

/// <summary>https://drafts.csswg.org/css-display/#layout-specific-display</summary>
internal enum DisplayInternal : byte
{
    /// <summary>Not an internal display type.</summary>
    None,
    TableRowGroup,
    TableHeaderGroup,
    TableFooterGroup,
    TableRow,
    TableCell,
    TableColumnGroup,
    TableColumn,
    TableCaption,
    RubyBase,
    RubyText,
    RubyBaseContainer,
    RubyTextContainer,
}

/// <summary>
/// The computed value of display (https://drafts.csswg.org/css-display/#the-display-properties) as its components: the
/// box generation (<see cref="DisplayBox"/>), the outer and inner display types, the list-item flag and the internal
/// display types of tables and ruby. The computed value is already blockified when needed (root element, floats,
/// absolutely positioned boxes, flex and grid items).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Display(DisplayBox Box, DisplayOutside Outside, DisplayInside Inside, DisplayInternal Internal = DisplayInternal.None, bool IsListItem = false)
{
    /// <summary>
    /// Whether the display is <c>layout(name)</c> (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers): the
    /// box is a layout API container. For the other members, it is a block container that establishes a block formatting
    /// context (<see cref="DisplayInside.FlowRoot"/>): "This is the same as establishing a block formatting context, except
    /// that the layout provided by the author is used instead of the block layout" (when the layout is registered, see
    /// <see cref="ContainerBox.LayoutApi"/>; else the box falls back to the flow layout).
    /// </summary>
    public bool IsLayoutFunction { get; init; }

    public static Display None => new(DisplayBox.None, DisplayOutside.Inline, DisplayInside.Flow);

    public static Display Contents => new(DisplayBox.Contents, DisplayOutside.Inline, DisplayInside.Flow);

    public static Display Inline => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flow);

    public static Display Block => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow);

    public bool IsNone => Box == DisplayBox.None;

    public bool IsContents => Box == DisplayBox.Contents;

    /// <summary>Whether the element generates a principal box (display is neither none nor contents).</summary>
    public bool GeneratesBox => Box == DisplayBox.Normal;

    /// <summary>Whether the display type is a layout-internal type of a table or of ruby.</summary>
    public bool IsInternal => Internal != DisplayInternal.None;

    /// <summary>Whether the display type is an internal table box other than the caption (https://drafts.csswg.org/css-tables-3/#internal-table-box).</summary>
    public bool IsInternalTableBox => Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup or DisplayInternal.TableRow
        or DisplayInternal.TableCell or DisplayInternal.TableColumnGroup or DisplayInternal.TableColumn;

    /// <summary>Whether the display type is an internal ruby box (https://drafts.csswg.org/css-ruby-1/#internal-ruby-box).</summary>
    public bool IsInternalRubyBox => Internal is DisplayInternal.RubyBase or DisplayInternal.RubyText or DisplayInternal.RubyBaseContainer or DisplayInternal.RubyTextContainer;

    /// <summary>Whether the principal box is a row group (table-row-group, table-header-group, table-footer-group).</summary>
    public bool IsTableRowGroup => Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup;

    /// <summary>Whether the principal box is block-level (https://drafts.csswg.org/css-display/#block-level), including the table captions.</summary>
    public bool IsBlockLevel => Box == DisplayBox.Normal && Outside == DisplayOutside.Block && (Internal is DisplayInternal.None or DisplayInternal.TableCaption);

    /// <summary>Whether the principal box is inline-level (https://drafts.csswg.org/css-display/#inline-level).</summary>
    public bool IsInlineLevel => Box == DisplayBox.Normal && Outside == DisplayOutside.Inline && Internal == DisplayInternal.None;

    /// <summary>
    /// Whether the principal box is an inline box (https://drafts.csswg.org/css-display/#inline-box): a non-replaced
    /// inline-level box whose inner display type is flow (the ruby containers are inline boxes too), or an internal ruby box
    /// (https://drafts.csswg.org/css-ruby-1/#ruby-display: the ruby bases, annotations and their containers are laid out by
    /// the inline layout of the ruby container, as inline boxes).
    /// </summary>
    public bool IsInlineBox => (IsInlineLevel && Inside is DisplayInside.Flow or DisplayInside.Ruby) || (Box == DisplayBox.Normal && IsInternalRubyBox);

    /// <summary>
    /// Whether the principal box is an atomic inline (https://drafts.csswg.org/css-display/#atomic-inline): an inline-level
    /// box that is not an inline box (inline-block, inline-table, inline-flex, inline-grid, inline math...). The replaced
    /// elements are atomic inlines too, whatever their display type.
    /// </summary>
    public bool IsAtomicInline => IsInlineLevel && Inside is not (DisplayInside.Flow or DisplayInside.Ruby);

    /// <summary>
    /// Whether the principal box is a block container (https://drafts.csswg.org/css-display/#block-container): its inner
    /// display type is flow or flow-root (the table cells and captions are block containers), and it is not an inline box.
    /// </summary>
    public bool IsBlockContainer => Box == DisplayBox.Normal && Inside is DisplayInside.Flow or DisplayInside.FlowRoot && !IsInlineBox
        && Internal is DisplayInternal.None or DisplayInternal.TableCell or DisplayInternal.TableCaption;

    /// <summary>Whether the box is a flex container (flex, inline-flex, or the legacy -webkit-box).</summary>
    public bool IsFlexContainer => Box == DisplayBox.Normal && Inside is DisplayInside.Flex or DisplayInside.WebkitBox && Internal == DisplayInternal.None;

    /// <summary>Whether the box is a grid container (grid, inline-grid, grid-lanes).</summary>
    public bool IsGridContainer => Box == DisplayBox.Normal && Inside is DisplayInside.Grid or DisplayInside.GridLanes && Internal == DisplayInternal.None;

    /// <summary>Whether the principal box is a table wrapper box (table, inline-table).</summary>
    public bool IsTable => Box == DisplayBox.Normal && Inside == DisplayInside.Table && Internal == DisplayInternal.None;

    /// <summary>
    /// Parses the computed value of display, which is canonical (https://drafts.csswg.org/css-display/#display-value-summary,
    /// see <see cref="Values.CssDisplay.Canonicalize"/>): e.g. <c>block</c>, <c>inline-block</c>, <c>flow-root list-item</c>,
    /// <c>inline flex</c> is <c>inline-flex</c>.
    /// </summary>
    public static Display Parse(string value) => value switch
    {
        "none" => None,
        "contents" => Contents,
        "inline" => Inline,
        "block" => Block,
        "flow-root" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot),
        "inline-block" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.FlowRoot),
        "run-in" => new(DisplayBox.Normal, DisplayOutside.RunIn, DisplayInside.Flow),
        "list-item" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, IsListItem: true),
        "flex" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flex),
        "inline-flex" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flex),
        "grid" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Grid),
        "inline-grid" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Grid),
        "grid-lanes" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.GridLanes),
        "inline-grid-lanes" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.GridLanes),
        "table" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Table),
        "inline-table" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Table),
        "ruby" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Ruby),
        "math" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Math),
        "-webkit-box" => new(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.WebkitBox),
        "-webkit-inline-box" => new(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.WebkitBox),

        // https://drafts.csswg.org/css-tables-3/#table-structure: the cells and the captions are block containers
        "table-row-group" => CreateInternal(DisplayInternal.TableRowGroup),
        "table-header-group" => CreateInternal(DisplayInternal.TableHeaderGroup),
        "table-footer-group" => CreateInternal(DisplayInternal.TableFooterGroup),
        "table-row" => CreateInternal(DisplayInternal.TableRow),
        "table-cell" => CreateInternal(DisplayInternal.TableCell, DisplayInside.FlowRoot),
        "table-column-group" => CreateInternal(DisplayInternal.TableColumnGroup),
        "table-column" => CreateInternal(DisplayInternal.TableColumn),
        "table-caption" => CreateInternal(DisplayInternal.TableCaption, DisplayInside.FlowRoot),

        // https://drafts.csswg.org/css-ruby-1/#ruby-display: the internal ruby boxes are inline-level
        "ruby-base" => CreateInternal(DisplayInternal.RubyBase, outside: DisplayOutside.Inline),
        "ruby-text" => CreateInternal(DisplayInternal.RubyText, outside: DisplayOutside.Inline),
        "ruby-base-container" => CreateInternal(DisplayInternal.RubyBaseContainer, outside: DisplayOutside.Inline),
        "ruby-text-container" => CreateInternal(DisplayInternal.RubyTextContainer, outside: DisplayOutside.Inline),

        // https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers: layout(name) and inline layout(name)
        _ when Values.CssDisplay.IsLayoutFunction(value) => new(DisplayBox.Normal, value.StartsWith("inline ", StringComparison.Ordinal) ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot) { IsLayoutFunction = true },
        _ => ParseMultipleKeywords(value),
    };

    private static Display CreateInternal(DisplayInternal type, DisplayInside inside = DisplayInside.Flow, DisplayOutside outside = DisplayOutside.Block) => new(DisplayBox.Normal, outside, inside, type);

    /// <summary>Parses the multi-keyword values: <c>&lt;display-outside&gt; &lt;display-inside&gt;</c> and the list items.</summary>
    private static Display ParseMultipleKeywords(string value)
    {
        DisplayOutside? outside = null;
        DisplayInside? inside = null;
        var listItem = false;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (keyword)
            {
                case "block":
                    outside = DisplayOutside.Block;
                    break;
                case "inline":
                    outside = DisplayOutside.Inline;
                    break;
                case "run-in":
                    outside = DisplayOutside.RunIn;
                    break;
                case "list-item":
                    listItem = true;
                    break;
                case "flow":
                    inside = DisplayInside.Flow;
                    break;
                case "flow-root":
                    inside = DisplayInside.FlowRoot;
                    break;
                case "table":
                    inside = DisplayInside.Table;
                    break;
                case "flex":
                    inside = DisplayInside.Flex;
                    break;
                case "grid":
                    inside = DisplayInside.Grid;
                    break;
                case "grid-lanes":
                    inside = DisplayInside.GridLanes;
                    break;
                case "ruby":
                    inside = DisplayInside.Ruby;
                    break;
                case "math":
                    inside = DisplayInside.Math;
                    break;
                default:
                    // An unknown value: like the initial value
                    return Inline;
            }
        }

        // https://drafts.csswg.org/css-display/#outer-role: the outer display type defaults to block, except for ruby and math
        var innerType = inside ?? DisplayInside.Flow;
        var outerType = outside ?? (innerType is DisplayInside.Ruby or DisplayInside.Math ? DisplayOutside.Inline : DisplayOutside.Block);
        return new Display(DisplayBox.Normal, outerType, innerType, IsListItem: listItem);
    }
}
