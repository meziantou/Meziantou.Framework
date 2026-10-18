using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The boxes of the select elements rendered with base appearance (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2).
// The specification describes shadow trees of the user agent; the boxes are generated here instead:
// - a drop-down box holds its button (the first child element when it is a button, else the select fallback button text, an
//   anonymous block whose text is the label of the selected option), then its ::picker-icon; the select popover is the box
//   of its ::picker(select) pseudo-element, which holds the boxes of the other children. It is a popover: its box only
//   exists while the picker is open, in the top layer, anchored to the select element;
// - a list box holds the boxes of its children;
// - an option rendered with base appearance holds its ::checkmark (before its ::before), then its children, or its label when it has a label
//   attribute; an optgroup holds its legend (its first child element when it is a legend, else the optgroup label element,
//   an anonymous block whose text is its label attribute), then its other children.
internal sealed partial class BoxTreeBuilder
{
    /// <summary>The select elements whose select popover is showing, reached while the tree was built (see <see cref="BuildTopLayer"/>).</summary>
    private HashSet<Element>? _reachedPickers;

    /// <summary>The style of the optgroup label element (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2: "padding-inline: 0.5em; min-block-size: 1lh").</summary>
    private static readonly CssLonghandValue[] OptgroupLabelStyle =
    [
        new("display", "block"),
        new("padding-inline-start", "0.5em"),
        new("padding-inline-end", "0.5em"),
        new("min-block-size", "1lh"),
    ];

    /// <summary>The elements rendered in the top layer, which are not rendered at their position in the tree: all but the select elements whose entry is their picker.</summary>
    private static HashSet<Element> CreateDeferredTopLayer(IReadOnlyList<Element> topLayer, Element root)
    {
        var deferred = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        var document = root.NodeDocument;
        foreach (var element in topLayer)
        {
            // https://drafts.csswg.org/css-position-4/#overlay: an element of the top layer whose overlay is none (e.g. while it
            // transitions to auto) generates its boxes at its place in the document
            if (!ReferenceEquals(element, root) && !Document.IsTopLayerPicker(element) && document.IsRenderedInTopLayer(element, document.GetStyleEngine().GetStyle(element).GetComputedValue("overlay")))
            {
                deferred.Add(element);
            }
        }

        return deferred;
    }

    /// <summary>
    /// Whether the children of the element are built by <see cref="BuildBaseAppearanceChildren"/>: a drop-down box with base
    /// appearance, and the options and the groups that are rendered (with base appearance, or in a list box with native appearance).
    /// </summary>
    private bool HasBaseAppearanceChildren(Element element, ComputedStyle style) => element.TagId switch
    {
        TagId.Select => element is HtmlSelectElement select && StyleEngine.HasBaseAppearance(style) && StyleEngine.IsDropDownBox(select),
        // (the options and the groups of a list box with native appearance are boxes too: its rows, see FormControlContents)
        TagId.Option or TagId.Optgroup => element.IsHtmlNamespace && style.SelectStyle is { } selectStyle
            && (_engine.AreContentsRenderedWithBaseAppearance(selectStyle) || FormControlContents.IsNativeListBox(selectStyle.Element, selectStyle))
            && ReferenceEquals(HtmlOptionElement.GetNearestAncestorSelect(element), selectStyle.Element),
        _ => false,
    };

    private void BuildBaseAppearanceChildren(Element element, ComputedStyle style, ContainerBox contents)
    {
        switch (element)
        {
            case HtmlSelectElement select:
                BuildSelectButton(select, contents);

                // The select popover follows the button in the shadow tree: while its picker is open, it is rendered in the
                // top layer (see BuildTopLayer); otherwise it is not rendered (display: none in the user agent style sheet),
                // unless the author gives it another display
                if (_engine.IsSelectPickerRendered(style) && !IsInSkippedContents(contents))
                {
                    if (select.IsPickerOpen && Document.IsTopLayerPicker(select))
                    {
                        (_reachedPickers ??= new HashSet<Element>(ReferenceEqualityComparer.Instance)).Add(select);
                    }
                    else
                    {
                        BuildSelectPicker(select, contents, inTopLayer: false);
                    }
                }

                break;

            case HtmlOptionElement option:
                // A row of a list box with native appearance shows the label of the option
                // (https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-label), as in the browsers: its
                // children are not rendered
                if (style.SelectStyle is { } optionSelectStyle && !StyleEngine.HasBaseAppearance(optionSelectStyle))
                {
                    var text = option.GetLabel(includeAltText: true);
                    if (text.Length > 0)
                    {
                        contents.AppendChild(new TextRun(style, option, pseudoElement: null, TextRunKind.Generated, node: null, text, 0, text.Length));
                    }

                    break;
                }

                // "If the option element has a label attribute whose value is not the empty string, then the option contents
                // slot is expected to not take any nodes": the option label element, a span, is rendered
                if (option.GetAttributeValue("label") is { Length: > 0 } label)
                {
                    contents.AppendChild(new TextRun(style, option, pseudoElement: null, TextRunKind.Generated, node: null, label, 0, label.Length));
                }
                else
                {
                    BuildChildNodes(option, style, contents);
                }

                break;

            default:
                BuildOptgroupChildren(element, style, contents);
                break;
        }
    }

    /// <summary>
    /// The contents of the box of a drop-down box with base appearance: its button, the first child element when it is a
    /// button element (the user agent style sheet gives it <c>display: contents</c>), else the select fallback button text.
    /// The other children are in the select popover (see <see cref="BuildSelectPicker"/>).
    /// </summary>
    private void BuildSelectButton(HtmlSelectElement select, ContainerBox contents)
    {
        if (select.FirstElementChild is { } first && StyleEngine.IsSelectButton(first))
        {
            BuildElement(first, contents);
            return;
        }

        // https://html.spec.whatwg.org/multipage/form-elements.html#clone-selected-option-into-select-button
        var text = "";
        foreach (var option in select.GetListOfOptions())
        {
            if (option.Selectedness)
            {
                text = option.GetLabel(includeAltText: false);
                break;
            }
        }

        var box = CreateAnonymousBox(contents, Display.Block);
        contents.AppendChild(box);
        if (text.Length > 0)
        {
            box.AppendChild(new TextRun(box.Style, select, pseudoElement: null, TextRunKind.Generated, node: null, text, 0, text.Length));
        }

        FixupContainer(box);
    }

    /// <summary>
    /// The boxes that follow the <c>::after</c> pseudo-element of a drop-down box with base appearance: its <c>::picker-icon</c>
    /// (https://drafts.csswg.org/css-forms-1/#picker-icon: "It generates a box as if it was a child of its originating
    /// element, after any boxes generated by the ::after pseudo-element").
    /// </summary>
    private void BuildSelectTrailingBoxes(HtmlSelectElement select, ContainerBox contents) => BuildPseudoElement(select, StyleEngine.PickerIcon, contents);

    /// <summary>
    /// Builds the box of the select popover of a select element (its <c>::picker(select)</c> pseudo-element): it holds the
    /// boxes of the children of the select element other than its button. It is a child of the viewport while the picker is
    /// open (the top layer), and of the box of the select element otherwise.
    /// </summary>
    private ContainerBox? BuildSelectPicker(HtmlSelectElement select, ContainerBox parent, bool inTopLayer)
    {
        var style = _engine.GetStyle(select, StyleEngine.SelectPicker);
        var display = style.Layout.Display;
        if (display.IsNone || display.IsContents)
            return null;

        var box = CreatePrincipalBox(style, display, select, StyleEngine.SelectPicker, parent, out var contents);
        box.IsInTopLayer = inTopLayer;
        _boxes[(select, StyleEngine.SelectPicker)] = box;
        if (contents.IsBlockContainer)
        {
            // The ::first-line and ::first-letter pseudo-elements of the picker exist when a rule styles them
            if (_engine.GetStyle(select, StyleEngine.SelectPickerPrefix + "first-line") is { HasCascadedValues: true } firstLineStyle)
            {
                contents.FirstLineStyle = firstLineStyle;
            }

            if (_engine.GetStyle(select, StyleEngine.SelectPickerPrefix + "first-letter").HasCascadedValues)
            {
                _firstLetterContainers.Add(contents);
            }
        }

        BuildPseudoElementBox(select, StyleEngine.SelectPickerPrefix + "before", contents);
        for (var node = select.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is Element element && StyleEngine.IsSelectButton(element))
                continue;

            BuildNode(node, select, style, contents);
        }

        BuildPseudoElementBox(select, StyleEngine.SelectPickerPrefix + "after", contents);

        FixupContainer(contents);
        if (!ReferenceEquals(box, contents))
        {
            FixupContainer(box);
        }

        return box;
    }

    /// <summary>
    /// The contents of an optgroup rendered with base appearance or in a list box with native appearance: its legend (the
    /// first child element when it is a legend element), else the optgroup label element, then its other children.
    /// </summary>
    private void BuildOptgroupChildren(Element optgroup, ComputedStyle style, ContainerBox contents)
    {
        // In a list box with native appearance, the label of the group is a row: the text of its legend, else its label
        // attribute (https://html.spec.whatwg.org/multipage/form-elements.html#concept-optgroup-label); the legend is not rendered
        var native = style.SelectStyle is { } selectStyle && !StyleEngine.HasBaseAppearance(selectStyle);
        var legend = optgroup.FirstElementChild is { } first && first.IsHtml(TagId.Legend) ? first : null;
        if (legend is null || native)
        {
            var label = legend is null ? optgroup.GetAttributeValue("label") ?? "" : Infra.InfraString.StripAndCollapseAsciiWhitespace(legend.TextContent ?? "");
            var labelDeclarations = native ? NativeOptgroupLabelStyle : OptgroupLabelStyle;
            var labelStyle = GetAnonymousStyle(style).GetDerivedStyle(labelDeclarations, labelDeclarations, static (style, declarations) => style.WithOverrides(declarations));
            var box = new ContainerBox(ContainerBoxKind.Anonymous, labelStyle, Display.Block, element: null);
            contents.AppendChild(box);
            if (label.Length > 0)
            {
                box.AppendChild(new TextRun(labelStyle, optgroup, pseudoElement: null, TextRunKind.Generated, node: null, label, 0, label.Length));
            }

            FixupContainer(box);
        }

        if (legend is not null && native)
        {
            for (var node = optgroup.FirstChild; node is not null; node = node.NextSibling)
            {
                if (!ReferenceEquals(node, legend))
                {
                    BuildNode(node, optgroup, style, contents);
                }
            }

            return;
        }

        BuildChildNodes(optgroup, style, contents);
    }
}
