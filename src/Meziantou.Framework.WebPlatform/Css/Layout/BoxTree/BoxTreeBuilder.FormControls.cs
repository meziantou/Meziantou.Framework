using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The boxes of the parts of the form controls that are replaced boxes (see FormControlContents): the specification leaves
// them to the user agent, and the browsers use shadow trees of the user agent; the boxes are generated here instead, in an
// anonymous block container that is the child of the replaced box of the control.
internal sealed partial class BoxTreeBuilder
{
    /// <summary>The style of the label of an optgroup of a list box with native appearance: a row.</summary>
    private static readonly CssLonghandValue[] NativeOptgroupLabelStyle =
    [
        new("display", "block"),
        new("min-block-size", "1lh"),
        new("white-space-collapse", "collapse"),
        new("text-wrap-mode", "nowrap"),
    ];

    /// <summary>Builds the contents box of the replaced box of a form control whose parts are boxes (see <see cref="FormControlContents"/>).</summary>
    private void BuildFormControlContents(ContainerBox control, Element element, ComputedStyle style)
    {
        if (FormControlContents.IsNativeListBox(element, style))
        {
            // The rows of a list box: the boxes of the child elements of the select element (its options, its groups, and
            // the other elements that its content model allows or not); as in the browsers, its text is not rendered
            var contents = CreateFormControlContents(control);
            for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                BuildElement(child, contents);
            }

            FixupContainer(contents);
        }
        else if (FormControlContents.HasSliderParts(element, style))
        {
            // The contents box is the containing block of the thumb, which is absolutely positioned
            var contents = CreateFormControlContents(control, SliderContentsStyle);
            BuildSliderParts(element, style, contents);
            FixupContainer(contents);
        }
    }

    private static readonly CssLonghandValue[] SliderContentsStyle = [new("position", "relative")];

    /// <summary>
    /// Builds the boxes of the parts of a slider-like control (https://drafts.csswg.org/css-forms-1/#slider-pseudos): its
    /// <c>::slider-track</c>, which holds its <c>::slider-fill</c>, then its <c>::slider-thumb</c>. The user agent gives the
    /// fill the inline size of the progress of the control, as a percentage of the track, and moves the thumb along the
    /// inline axis of its containing block (the thumb is absolutely positioned in the user agent style sheet): its
    /// inline-start inset is the progress, and it is translated back by the same fraction of its own size, so that it stays
    /// in its containing block. These declarations replace the ones of the author.
    /// </summary>
    private void BuildSliderParts(Element element, ComputedStyle style, ContainerBox parent)
    {
        var writingDirection = WritingDirection.FromStyle(style.Layout);
        var progress = (FormControlContents.GetSliderProgress(element) * 100).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "%";
        var trackStyle = _engine.GetStyle(element, FormControlContents.SliderTrack);
        var trackDisplay = trackStyle.Layout.Display;
        if (!trackDisplay.IsNone)
        {
            var trackContents = parent;
            ContainerBox? track = null;
            if (!trackDisplay.IsContents)
            {
                track = CreatePrincipalBox(trackStyle, trackDisplay, element, FormControlContents.SliderTrack, parent, out trackContents);
                _boxes[(element, FormControlContents.SliderTrack)] = track;
            }

            var fillStyle = _engine.GetStyle(element, FormControlContents.SliderFill).GetDerivedStyleWithOverrides([new CssLonghandValue(writingDirection.IsHorizontal ? "width" : "height", progress)]);
            var fillDisplay = fillStyle.Layout.Display;
            if (!fillDisplay.IsNone && !fillDisplay.IsContents)
            {
                var fill = CreatePrincipalBox(fillStyle, fillDisplay, element, FormControlContents.SliderFill, trackContents, out var fillContents);
                _boxes[(element, FormControlContents.SliderFill)] = fill;
                FixupContainer(fillContents);
                if (!ReferenceEquals(fill, fillContents))
                {
                    FixupContainer(fill);
                }
            }

            if (track is not null)
            {
                FixupContainer(trackContents);
                if (!ReferenceEquals(track, trackContents))
                {
                    FixupContainer(track);
                }
            }
        }

        var (inset, translate) = writingDirection.InlineStart switch
        {
            PhysicalSide.Left => ("left", "-" + progress + " 0"),
            PhysicalSide.Right => ("right", progress + " 0"),
            PhysicalSide.Top => ("top", "0 -" + progress),
            _ => ("bottom", "0 " + progress),
        };
        var thumbStyle = _engine.GetStyle(element, FormControlContents.SliderThumb).GetDerivedStyleWithOverrides([new CssLonghandValue(inset, progress), new CssLonghandValue("translate", translate)]);
        var thumbDisplay = thumbStyle.Layout.Display;
        if (!thumbDisplay.IsNone && !thumbDisplay.IsContents)
        {
            var thumb = CreatePrincipalBox(thumbStyle, thumbDisplay, element, FormControlContents.SliderThumb, parent, out var thumbContents);
            _boxes[(element, FormControlContents.SliderThumb)] = thumb;
            FixupContainer(thumbContents);
            if (!ReferenceEquals(thumb, thumbContents))
            {
                FixupContainer(thumb);
            }
        }
    }

    /// <summary>
    /// Builds the contents of the box of a form control that is not a replaced box and whose contents are generated (see
    /// <see cref="FormControlContents.HasGeneratedContents"/>): a file upload control, and, with base appearance
    /// (https://drafts.csswg.org/css-forms-1/#pseudo-elements), the label of a button, the <c>::color-swatch</c> of a color
    /// well and the parts of a slider-like control.
    /// </summary>
    private void BuildGeneratedControlContents(Element element, ComputedStyle style, ContainerBox contents)
    {
        switch (element)
        {
            case HtmlInputElement { InputType: InputType.File } fileUpload:
                BuildFileUploadContents(fileUpload, style, contents);
                break;

            case HtmlInputElement { InputType: InputType.Button or InputType.Submit or InputType.Reset } button:
            {
                // (the line feeds of the label break its lines, as with native appearance: they are preserved)
                var label = FormControlContents.GetButtonLabel(button);
                if (label.Length > 0)
                {
                    var labelStyle = label.Contains('\n', StringComparison.Ordinal) ? GetAnonymousStyle(style).GetDerivedStyle(ButtonLabelStyle, 0, static (style, _) => style.WithOverrides(ButtonLabelStyle)) : style;
                    contents.AppendChild(new TextRun(labelStyle, element, pseudoElement: null, TextRunKind.Generated, node: null, label, 0, label.Length));
                }

                break;
            }

            case HtmlInputElement { InputType: InputType.Color } color:
            {
                // "background: linear-gradient(control-value()), linear-gradient(to bottom right, black 50%, white 50%)": the
                // color of the control over a pattern that shows its alpha channel (control-value() is not supported: the
                // declaration replaces the one of the author)
                var value = Rendering.DisplayColor.Parse(color.Value) is null ? "black" : color.Value;
                var swatchStyle = _engine.GetStyle(element, StyleEngine.ColorSwatch).GetDerivedStyleWithOverrides([new CssLonghandValue("background-image", "linear-gradient(" + value + ", " + value + "), linear-gradient(to bottom right, black 50%, white 50%)")]);
                var display = swatchStyle.Layout.Display;
                if (!display.IsNone && !display.IsContents)
                {
                    var swatch = CreatePrincipalBox(swatchStyle, display, element, StyleEngine.ColorSwatch, contents, out var swatchContents);
                    _boxes[(element, StyleEngine.ColorSwatch)] = swatch;
                    FixupContainer(swatchContents);
                    if (!ReferenceEquals(swatch, swatchContents))
                    {
                        FixupContainer(swatch);
                    }
                }

                break;
            }

            default:
                BuildSliderParts(element, style, contents);
                break;
        }
    }

    private static readonly CssLonghandValue[] ButtonLabelStyle = [new("white-space-collapse", "preserve")];

    /// <summary>
    /// Builds the contents of a file upload control (https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-file-upload-control:
    /// "an 'inline-block' box containing a span of text giving the filename(s) of the selected files, if any, followed by a
    /// button that, when activated, provides the user with a file selection dialog"; as in the browsers, the button comes
    /// first): a line that does not wrap, with the box of the <c>::file-selector-button</c> pseudo-element
    /// (https://drafts.csswg.org/css-forms-1/#file-selector-button-pseudo), whose text is its label, then the text.
    /// </summary>
    private void BuildFileUploadContents(HtmlInputElement input, ComputedStyle style, ContainerBox parent)
    {
        var lineStyle = GetAnonymousStyle(style).GetDerivedStyle(FileUploadLineStyle, 0, static (style, _) => style.WithOverrides(FileUploadLineStyle));
        var line = new ContainerBox(ContainerBoxKind.Anonymous, lineStyle, Display.Block, element: null);
        parent.AppendChild(line);

        var buttonStyle = _engine.GetStyle(input, FormControlContents.FileSelectorButton);
        var buttonDisplay = buttonStyle.Layout.Display;
        if (!buttonDisplay.IsNone)
        {
            var buttonContents = line;
            ContainerBox? button = null;
            if (!buttonDisplay.IsContents)
            {
                button = CreatePrincipalBox(buttonStyle, buttonDisplay, input, FormControlContents.FileSelectorButton, line, out buttonContents);
                _boxes[(input, FormControlContents.FileSelectorButton)] = button;
            }

            var label = FormControlContents.GetFileSelectorButtonLabel(input);
            buttonContents.AppendChild(new TextRun(buttonStyle, input, FormControlContents.FileSelectorButton, TextRunKind.Generated, node: null, label, 0, label.Length));
            if (button is not null)
            {
                FixupContainer(buttonContents);
                if (!ReferenceEquals(button, buttonContents))
                {
                    FixupContainer(button);
                }
            }
        }

        var text = FormControlContents.GetFileUploadText(input);
        line.AppendChild(new TextRun(lineStyle, input, pseudoElement: null, TextRunKind.Generated, node: null, text, 0, text.Length));
        FixupContainer(line);
    }

    /// <summary>The style of the line of a file upload control: it does not wrap.</summary>
    private static readonly CssLonghandValue[] FileUploadLineStyle =
    [
        new("white-space-collapse", "collapse"),
        new("text-wrap-mode", "nowrap"),
    ];

    private ContainerBox CreateFormControlContents(ContainerBox control, ReadOnlySpan<CssLonghandValue> declarations = default)
    {
        var style = GetAnonymousStyle(control.Style);
        if (declarations.Length > 0)
        {
            style = style.GetDerivedStyleWithOverrides(declarations.ToArray());
        }

        var contents = new ContainerBox(ContainerBoxKind.Anonymous, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot), element: null) { IsFormControlContents = true };
        control.AppendChild(contents);
        return contents;
    }
}
