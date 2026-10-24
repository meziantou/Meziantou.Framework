using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The styles of the select element that depend on its rendering (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2):
// a select element is rendered as a drop-down box or as a list box, with native (or primitive) appearance or with base
// appearance (the computed value of its appearance property is base-select), and its contents are rendered with base
// appearance when its picker has base appearance too. The specification describes shadow trees of the user agent; the
// library has none: the select popover is the box of the ::picker(select) pseudo-element, which the children of a
// drop-down box other than its button inherit from (they are slotted in it).
internal sealed partial class StyleEngine
{
    /// <summary>The name of the <c>::picker(select)</c> pseudo-element (https://drafts.csswg.org/css-forms-1/#picker-pseudo), the select popover.</summary>
    internal const string SelectPicker = "picker(select)";

    /// <summary>
    /// The prefix of the names of the pseudo-elements of <c>::picker(select)</c>, an element-backed pseudo-element
    /// (https://drafts.csswg.org/css-forms-1/#picker-pseudo): "picker(select)::before", "picker(select)::after",
    /// "picker(select)::backdrop", "picker(select)::first-line", "picker(select)::first-letter" and the highlight
    /// pseudo-elements (e.g. "picker(select)::selection", "picker(select)::highlight(name)"; the other sub-pseudo-elements
    /// are not supported).
    /// </summary>
    internal const string SelectPickerPrefix = SelectPicker + "::";

    /// <summary>The name of the <c>::picker-icon</c> pseudo-element (https://drafts.csswg.org/css-forms-1/#picker-icon).</summary>
    internal const string PickerIcon = "picker-icon";

    /// <summary>The name of the <c>::checkmark</c> pseudo-element (https://drafts.csswg.org/css-forms-1/#checkmark).</summary>
    internal const string Checkmark = "checkmark";

    /// <summary>
    /// The rules of the user agent for the select popover (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2:
    /// a div element of the shadow tree that is a popover): the rules of the div elements and of the popovers of the user
    /// agent style sheet (https://html.spec.whatwg.org/multipage/rendering.html#flow-content-3), which the rules of
    /// <c>::picker(select)</c> override. A popover that is not showing is not rendered. The color of the select element is
    /// inherited: the system color that the library gives to the form controls, as the browsers, is for the native appearance
    /// (the specification does not set the color).
    /// </summary>
    private const string SelectPickerStyleSheet = """
        select { color: inherit; }
        ::picker(select) {
          display: block;
          unicode-bidi: isolate;
          position: fixed;
          inset: 0;
          width: fit-content;
          height: fit-content;
          margin: auto;
          border: solid;
          padding: 0.25em;
          overflow: auto;
          color: CanvasText;
          background-color: Canvas;
        }
        select:not(:open)::picker(select) { display: none; }
        ::picker(select)::backdrop {
          position: fixed;
          inset: 0;
          pointer-events: none !important;
          background-color: transparent;
        }
        """;

    /// <summary>
    /// A select element rendered as a list box is "expected to render as an inline-block box"
    /// (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2), as the drop-down boxes with native
    /// appearance.
    /// </summary>
    private const string SelectListBoxStyleSheet = "select { display: inline-block; }";

    /// <summary>
    /// The rules of the rows of a list box with native appearance or primitive appearance, which the specification leaves to
    /// the user agent ("the height necessary to contain as many rows for items as given by the element's display size"): the
    /// options are the rows, blocks of at least one line (the natural size of the list box is a line per row, see
    /// <see cref="Layout.FormControlSizing"/>; the rows are as wide as the widest one and do not wrap, see
    /// <see cref="Layout.FormControlContents"/>). A selected option is highlighted; as in Chrome, the highlight of the
    /// options of a focused list box cannot be overridden by the author, and the options of a group have the weight of the
    /// list box.
    /// </summary>
    private const string SelectListBoxNativeAppearanceStyleSheet = """
        select option { min-block-size: 1lh; }
        select optgroup option { font-weight: normal; }
        select option:checked { background-color: light-dark(#cecece, #545454); }
        select:focus option:checked {
          background-color: SelectedItem !important;
          color: SelectedItemText !important;
        }
        """;

    private static void AddSelectRules(CascadeRuleCollector collector)
    {
        collector.AddRules(ParseUserAgentStyleSheet(SelectListBoxStyleSheet), baseUrl: null);
        Add(SelectRuleCondition.DropDown, HtmlUserAgentStyleSheet.SelectDropDown);
        Add(SelectRuleCondition.NativeAppearance, HtmlUserAgentStyleSheet.SelectNativeAppearance);
        Add(SelectRuleCondition.DropDownNativeAppearance, HtmlUserAgentStyleSheet.SelectDropDownNativeAppearance);
        Add(SelectRuleCondition.BaseAppearance, HtmlUserAgentStyleSheet.SelectBaseAppearance);
        Add(SelectRuleCondition.DropDownBaseAppearance, SelectPickerStyleSheet);
        Add(SelectRuleCondition.DropDownBaseAppearance, HtmlUserAgentStyleSheet.SelectDropDownBaseAppearance);
        Add(SelectRuleCondition.ListBoxBaseAppearance, HtmlUserAgentStyleSheet.SelectListBoxBaseAppearance);
        Add(SelectRuleCondition.ListBoxNativeAppearance, SelectListBoxNativeAppearanceStyleSheet);
        collector.SelectCondition = SelectRuleCondition.None;

        void Add(SelectRuleCondition condition, string styleSheet)
        {
            collector.SelectCondition = condition;
            collector.AddRules(ParseUserAgentStyleSheet(styleSheet), baseUrl: null);
        }
    }

    /// <summary>The subject of the cascade that is running, for the conditions of the rules of the select element.</summary>
    private Element? _cascadeElement;
    private string? _cascadePseudoElement;
    private ComputedStyle? _cascadeParent;
    private SelectRenderingState? _cascadeSelectState;

    /// <summary>
    /// Whether the select element whose style is being computed has base appearance: its style is first computed with the
    /// rules of the native appearance, which gives the computed value of its appearance property (no rule of the user agent
    /// that depends on the appearance sets it), then with the rules of its appearance.
    /// </summary>
    private bool _selectHasBaseAppearance;

    [Flags]
    private enum SelectRenderingState : byte
    {
        /// <summary>The subject is not a select element, nor a pseudo-element or a descendant of one.</summary>
        None = 0,
        InSelect = 1,

        /// <summary>The subject is the select element or one of its pseudo-elements (not a descendant).</summary>
        IsSelect = 2,
        DropDown = 4,
        BaseAppearance = 8,
        ContentsBaseAppearance = 16,
    }

    private bool MatchesSelectCondition(SelectRuleCondition condition)
    {
        if (condition is SelectRuleCondition.ControlBaseAppearance or SelectRuleCondition.ControlNativeAppearance)
            return IsCascadeSubjectControlWithBaseAppearance() == (condition == SelectRuleCondition.ControlBaseAppearance);

        var state = _cascadeSelectState ??= GetSelectRenderingState();
        if ((state & SelectRenderingState.InSelect) == SelectRenderingState.None)
            return false;

        var isSelect = (state & SelectRenderingState.IsSelect) != SelectRenderingState.None;
        var dropDown = (state & SelectRenderingState.DropDown) != SelectRenderingState.None;
        var baseAppearance = (state & SelectRenderingState.BaseAppearance) != SelectRenderingState.None;
        return condition switch
        {
            SelectRuleCondition.DropDown => dropDown,
            SelectRuleCondition.NativeAppearance => !baseAppearance,
            SelectRuleCondition.DropDownNativeAppearance => !baseAppearance && dropDown,

            // "An option element is rendered with base appearance if [...] the select's contents are being rendered with base appearance"
            SelectRuleCondition.BaseAppearance => isSelect ? baseAppearance : (state & SelectRenderingState.ContentsBaseAppearance) != SelectRenderingState.None,
            SelectRuleCondition.DropDownBaseAppearance => baseAppearance && dropDown,
            SelectRuleCondition.ListBoxBaseAppearance => baseAppearance && !dropDown,
            SelectRuleCondition.ListBoxNativeAppearance => !baseAppearance && !dropDown,
            _ => true,
        };
    }

    private SelectRenderingState GetSelectRenderingState()
    {
        var element = _cascadeElement;
        if (element is HtmlSelectElement select && _cascadePseudoElement is null)
        {
            var baseAppearance = _selectHasBaseAppearance || (_styles.TryGetValue((element, null), out var current) && HasBaseAppearance(current));
            return SelectRenderingState.InSelect | SelectRenderingState.IsSelect
                | (IsDropDownBox(select) ? SelectRenderingState.DropDown : SelectRenderingState.None)
                | (baseAppearance ? SelectRenderingState.BaseAppearance : SelectRenderingState.None);
        }

        if (_cascadeParent?.SelectStyle is not { Element: HtmlSelectElement ancestor } selectStyle)
            return SelectRenderingState.None;

        var state = SelectRenderingState.InSelect;
        if (ReferenceEquals(ancestor, element))
        {
            state |= SelectRenderingState.IsSelect;
        }

        var dropDown = IsDropDownBox(ancestor);
        if (dropDown)
        {
            state |= SelectRenderingState.DropDown;
        }

        if (HasBaseAppearance(selectStyle))
        {
            state |= SelectRenderingState.BaseAppearance;

            // The style of the picker does not depend on the rendering of the contents
            if ((state & SelectRenderingState.IsSelect) == SelectRenderingState.None && AreContentsRenderedWithBaseAppearance(selectStyle))
            {
                state |= SelectRenderingState.ContentsBaseAppearance;
            }
        }

        return state;
    }

    /// <summary>
    /// Whether the select element is rendered as a drop-down box (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2):
    /// its multiple attribute is absent and its display size is 1 (0 too, as in the browsers). A select element with a
    /// multiple attribute and a display size of 1 "may render as a multi-select drop-down box if the platform supports it":
    /// it is rendered as a list box.
    /// </summary>
    internal static bool IsDropDownBox(HtmlSelectElement select) => !select.IsMultiple && select.DisplaySize <= 1;

    /// <summary>
    /// Whether the style of a select element or of its picker gives it base appearance (https://drafts.csswg.org/css-ui-4/#base-appearance):
    /// <c>appearance: base-select</c> or <c>appearance: base</c>.
    /// </summary>
    internal static bool HasBaseAppearance(ComputedStyle selectStyle) => selectStyle.GetComputedValue("appearance") is "base-select" or "base";

    /// <summary>
    /// Whether the select popover of a select element is rendered: the select element is a drop-down box with base
    /// appearance, and its <c>::picker(select)</c> pseudo-element has base appearance ("The select popover is only rendered when
    /// it is opted in to base appearance separately from the select element. Otherwise, a native picker is used").
    /// </summary>
    internal bool IsSelectPickerRendered(ComputedStyle selectStyle)
        => selectStyle.Element is HtmlSelectElement select && IsDropDownBox(select) && HasBaseAppearance(selectStyle) && HasBaseAppearance(GetStyle(select, SelectPicker));

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#select's-options-are-being-rendered-with-base-appearance</summary>
    internal bool AreContentsRenderedWithBaseAppearance(ComputedStyle selectStyle)
    {
        if (selectStyle.Element is not HtmlSelectElement select || !HasBaseAppearance(selectStyle))
            return false;

        return !IsDropDownBox(select) || (!select.IsMultiple && HasBaseAppearance(GetStyle(select, SelectPicker)));
    }

    /// <summary>
    /// Whether the element is the button of a select element rendered as a drop-down box (the element that the select button
    /// slot takes): "the first child element of the select if the first child element is a button".
    /// </summary>
    internal static bool IsSelectButton(Element element)
        => element.IsHtml(TagId.Button) && element.ParentNode is HtmlSelectElement && element.PreviousElementSibling is null;

    /// <summary>
    /// Whether the element is a child of a select element rendered as a drop-down box that is slotted in its select popover
    /// (the select popover slot takes "all child nodes of the select except for the first child button"): it inherits from
    /// the <c>::picker(select)</c> pseudo-element.
    /// </summary>
    internal static bool IsInSelectPicker(Element element)
        => element.ParentNode is HtmlSelectElement select && IsDropDownBox(select) && element.Slottable?.AssignedSlot is null && !IsSelectButton(element);

    /// <summary>
    /// Returns the name of a highlight pseudo-element (e.g. "selection") of the text of an element: the text that is a child of a
    /// select element rendered as a drop-down box is slotted in its select popover, whose highlight pseudo-elements are the ones
    /// of <c>::picker(select)</c> (e.g. "picker(select)::selection").
    /// </summary>
    internal static string GetTextHighlightPseudoElement(Text text, string highlight) => IsTextInSelectPicker(text) ? SelectPickerPrefix + highlight : highlight;

    /// <summary>Whether the text is a child of a select element rendered as a drop-down box that is slotted in its select popover (see <see cref="IsInSelectPicker"/>).</summary>
    internal static bool IsTextInSelectPicker(Text text) => text.ParentNode is HtmlSelectElement select && IsDropDownBox(select) && text.Slottable?.AssignedSlot is null;
}
