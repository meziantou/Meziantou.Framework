using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>Where the baseline of a form control is (https://drafts.csswg.org/css-inline-3/#baseline-source).</summary>
internal enum FormControlBaseline : byte
{
    /// <summary>The control has no baseline: it is aligned by the block-end edge of its margin box (a checkbox, a gauge...).</summary>
    None,

    /// <summary>The baseline of a line of text centered in the content box (a text field, a button, a drop-down box).</summary>
    CenteredLine,

    /// <summary>The baseline of the first line of text at the block-start edge of the content box (a multiline text control, a list box).</summary>
    FirstLine,

    /// <summary>
    /// The block-end edge of the padding box of a checkbox or a radio button with its native appearance in a horizontal writing
    /// mode (else the control has no baseline), as in the browsers (https://github.com/whatwg/html/issues/5065).
    /// </summary>
    PaddingBoxEnd,

    /// <summary>
    /// The block-end edge of the content box in a horizontal writing mode (else the control has no baseline): a color well, as
    /// in the browsers (https://github.com/whatwg/html/issues/5065).
    /// </summary>
    ContentBoxEnd,
}

/// <summary>
/// The natural size of the content box of a form control, in its writing mode, in the coordinates of the element (not
/// zoomed), and its baseline.
/// </summary>
/// <param name="InlineSize">The natural inline size of the content box.</param>
/// <param name="BlockSize">The natural block size of the content box.</param>
/// <param name="Baseline">Where the baseline is.</param>
/// <param name="Lines">The number of lines of text centered in the content box (<see cref="FormControlBaseline.CenteredLine"/>): the baseline is the one of the last line.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FormControlSize(double InlineSize, double BlockSize, FormControlBaseline Baseline, int Lines = 1);

/// <summary>
/// The natural sizes of the form controls, the elements with default preferred size of the rendering section of HTML
/// (https://html.spec.whatwg.org/multipage/rendering.html#widgets), computed from the metrics of their primary font: the
/// text fields (https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-text-entry-widget), the
/// multiline text controls, the drop-down and list boxes, the buttons, the gauges and the progress bars, and the
/// <c>field-sizing: content</c> sizes (https://drafts.csswg.org/css-ui-4/#field-sizing).
/// </summary>
/// <remarks>
/// The native appearance of the controls is not painted: the sizes are the ones of the contents of the controls, without the
/// borders and the paddings of the native appearance of the browsers (the user agent style sheet of the specification has
/// none), and the scrollbars of the multiline text controls and of the list boxes take no space (overlay scrollbars).
/// </remarks>
internal static class FormControlSizing
{
    /// <summary>The default size of a checkbox and of a radio button, as in the browsers.</summary>
    private const double CheckboxSize = 13;

    /// <summary>The default size of a switch (a checkbox with a <c>switch</c> attribute, see <see cref="FormControlContents.IsSwitch"/>): twice as long as a checkbox in its inline axis.</summary>
    private const double SwitchInlineSize = 2 * CheckboxSize;

    /// <summary>The default size of a slider (<c>input type=range</c>), as in Chrome.</summary>
    private const double RangeInlineSize = 129;

    private const double RangeBlockSize = 16;

    /// <summary>The default size of a color well (<c>input type=color</c>), as in Chrome.</summary>
    private const double ColorInlineSize = 50;

    private const double ColorBlockSize = 27;

    /// <summary>
    /// Returns the natural size of a form control, or null when the element is not a form control. The multiline text controls
    /// and the list boxes reserve the space of their classic scrollbars (<paramref name="scrollbarThickness"/>, unzoomed).
    /// </summary>
    public static FormControlSize? Get(Element element, ComputedStyle style, double scrollbarThickness = 0)
    {
        switch (element)
        {
            case HtmlInputElement { ImageButtonRepresentsImage: true }:
                // https://html.spec.whatwg.org/multipage/rendering.html#images-3: an image button that represents an image is
                // a replaced element rendered as the image
                return null;

            case HtmlInputElement input:
                return GetInputSize(input, style, IsFieldSizingContent(style));

            case HtmlTextAreaElement textArea:
                return GetTextAreaSize(textArea, style, IsFieldSizingContent(style), scrollbarThickness);

            case HtmlSelectElement select:
                return GetSelectSize(select, style, IsFieldSizingContent(style), scrollbarThickness);

            case HtmlMeterElement:
                // https://html.spec.whatwg.org/multipage/rendering.html#the-meter-element-2: "a 'block-size' of '1em' and an 'inline-size' of '5em'"
                return new FormControlSize(5 * style.FontSize, style.FontSize, FormControlBaseline.None);

            case HtmlProgressElement:
                // https://html.spec.whatwg.org/multipage/rendering.html#the-progress-element-2: "a 'block-size' of '1em' and an 'inline-size' of '10em'"
                return new FormControlSize(10 * style.FontSize, style.FontSize, FormControlBaseline.None);

            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-ui-4/#field-sizing</summary>
    internal static bool IsFieldSizingContent(ComputedStyle style) => style.GetComputedValue("field-sizing") == "content";

    private static FormControlSize GetInputSize(HtmlInputElement input, ComputedStyle style, bool fieldSizingContent)
    {
        var lineHeight = GetLineHeight(style);
        switch (input.InputType)
        {
            case InputType.Checkbox or InputType.Radio:
                return FormControlContents.IsSwitch(input)
                    ? new FormControlSize(SwitchInlineSize, CheckboxSize, FormControlBaseline.None)
                    : new FormControlSize(CheckboxSize, CheckboxSize, FormControlBaseline.PaddingBoxEnd);

            case InputType.Range:
                return new FormControlSize(RangeInlineSize, RangeBlockSize, FormControlBaseline.None);

            case InputType.Color:
                return new FormControlSize(ColorInlineSize, ColorBlockSize, FormControlBaseline.ContentBoxEnd);

            case InputType.Submit or InputType.Reset or InputType.Button:
                // https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-button: the text of the value
                // attribute, or a text derived from the type
                var lines = GetButtonLabelLines(input, style);
                double labelWidth = 0;
                foreach (var line in lines)
                {
                    labelWidth = Math.Max(labelWidth, style.MeasureText(line));
                }

                return new FormControlSize(labelWidth, lines.Length * lineHeight, FormControlBaseline.CenteredLine, lines.Length);

            case InputType.Image:
                // https://html.spec.whatwg.org/multipage/rendering.html#images-3: "a button whose content is the element's
                // alternative text [...] about one line in height and whatever width is necessary to render the text on one line"
                return new FormControlSize(style.MeasureText(GetRenderedText(input, style, input.GetImageButtonLabel())), lineHeight, FormControlBaseline.CenteredLine);

            case InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DateTimeLocal:
                // The width of the editable fields of the format of the browsers, and of the picker button (1em)
                return new FormControlSize(style.MeasureText(GetDateTimeFormat(input.InputType)) + style.FontSize, lineHeight, FormControlBaseline.CenteredLine);

            default:
                // https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-text-entry-widget: as in the
                // browsers, the line of a text field is at least as tall as the normal line height
                lineHeight = GetTextFieldLineHeight(style) / style.Layout.EffectiveZoom;
                if (fieldSizingContent)
                {
                    // The caret takes 1px at the end of the text. The placeholder that is shown (in its ::placeholder box, whose
                    // font may differ from the one of the control) is a part of the contents: its line may be wider and taller
                    var value = input.Value;
                    if (value.Length == 0 && TextControlLayout.GetPlaceholder(input) is { } placeholder)
                    {
                        var placeholderStyle = GetPlaceholderBoxStyle(input, style, multiline: false);
                        var placeholderLineHeight = GetTextFieldLineHeight(placeholderStyle) / placeholderStyle.Layout.EffectiveZoom;
                        return new FormControlSize(placeholderStyle.MeasureText(GetRenderedText(input, placeholderStyle, placeholder)) + 1, Math.Max(lineHeight, placeholderLineHeight), FormControlBaseline.CenteredLine);
                    }

                    var text = input.InputType == InputType.Password ? new string('•', value.Length) : value;
                    return new FormControlSize(style.MeasureText(GetRenderedText(input, style, text)) + 1, lineHeight, FormControlBaseline.CenteredLine);
                }

                var size = 20L;
                if (input.GetAttributeValue("size") is { } sizeValue && Microsyntaxes.TryParseNonNegativeInteger(sizeValue, out var parsedSize) && parsedSize > 0)
                {
                    size = parsedSize;
                }

                return new FormControlSize(ConvertCharacterWidthToPixels(style, size), lineHeight, FormControlBaseline.CenteredLine);
        }
    }

    /// <summary>The style of the box of the <c>::placeholder</c> of a text control (see <c>BoxTreeBuilder.BuildTextControlContents</c>).</summary>
    private static ComputedStyle GetPlaceholderBoxStyle(Element element, ComputedStyle style, bool multiline)
        => StyleEngine.CreatePlaceholderBoxStyle(element.NodeDocument.GetStyleEngine().GetStyle(element, TextControlLayout.PlaceholderPseudoElement), style, multiline);

    /// <summary>Returns the widest text of the fields of a date and time control, in the format of the browsers (it gives the width of the control).</summary>
    private static string GetDateTimeFormat(InputType type) => type switch
    {
        InputType.Date => "mm/dd/yyyy",
        InputType.Month => "September 0000",
        InputType.Week => "Week 00, 0000",
        InputType.Time => "00:00 AM",
        _ => "mm/dd/yyyy, 00:00 AM",
    };

    /// <summary>Returns the text of the empty fields of a date and time control (the placeholders of the format of the browsers), or null for the other controls.</summary>
    internal static string? GetEmptyDateTimeText(InputType type) => type switch
    {
        InputType.Date => "mm/dd/yyyy",
        InputType.Month => "--------- ----",
        InputType.Week => "Week --, ----",
        InputType.Time => "--:-- --",
        InputType.DateTimeLocal => "mm/dd/yyyy, --:-- --",
        _ => null,
    };

    /// <summary>
    /// Returns the text of a control as it is rendered: transformed by <c>text-transform</c>
    /// (https://drafts.csswg.org/css-text-4/#text-transform-property) of the control.
    /// </summary>
    internal static string GetRenderedText(Element element, ComputedStyle style, string text)
    {
        var transform = style.Layout.TextTransform & ~TextTransform.MathAuto;
        return transform == TextTransform.None || text.Length == 0 ? text : TextTransformer.Transform(text, transform, TextTransformer.GetCaseLanguage(element));
    }

    /// <summary>
    /// Returns the lines of the label of a button (<c>input type=submit|reset|button</c>), transformed by <c>text-transform</c>:
    /// as in the browsers (their user agent style sheets have <c>white-space: pre</c> for these controls, the one of the
    /// specification does not), the line feeds of the value break the lines.
    /// </summary>
    internal static string[] GetButtonLabelLines(HtmlInputElement input, ComputedStyle style)
    {
        var label = GetRenderedText(input, style, GetButtonLabel(input));
        return label.AsSpan().ContainsAny('\n', '\r') ? label.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n') : [label];
    }

    internal static string GetButtonLabel(HtmlInputElement input)
    {
        if (input.GetAttributeValue("value") is { } value)
            return value;

        return input.InputType switch
        {
            InputType.Submit => "Submit",
            InputType.Reset => "Reset",
            _ => "",
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2</summary>
    private static FormControlSize GetTextAreaSize(HtmlTextAreaElement textArea, ComputedStyle style, bool fieldSizingContent, double scrollbarThickness)
    {
        var lineHeight = GetLineHeight(style);
        if (fieldSizingContent)
        {
            // The lines of the value, or of the placeholder that is shown (in the font of its ::placeholder box). The block size
            // is the one of the lines once they are laid out in the inline size of the control, where they wrap (see
            // ReplacedFormattingContext.GetAutomaticBlockSize): this is the size of the lines that do not wrap
            var value = textArea.Value;
            var placeholder = value.Length == 0 ? TextControlLayout.GetPlaceholder(textArea) : null;
            var textStyle = placeholder is null ? style : GetPlaceholderBoxStyle(textArea, style, multiline: true);
            var text = GetRenderedText(textArea, textStyle, placeholder ?? value);
            double width = 0;
            var lines = 0;
            foreach (var line in text.AsSpan().EnumerateLines())
            {
                width = Math.Max(width, textStyle.MeasureText(line));
                lines++;
            }

            var textLineHeight = placeholder is null ? lineHeight : Math.Max(lineHeight, GetLineHeight(textStyle));
            return new FormControlSize(width + 1, Math.Max(1, lines) * textLineHeight, FormControlBaseline.FirstLine);
        }

        // "The textarea effective width of a textarea element is size×avg + sbw [...] The textarea effective height of a
        // textarea element is the height in CSS pixels of the number of lines given by the element's character height, plus the
        // height of a scrollbar in CSS pixels." As in the browsers, the scrollbar of the block axis (that takes inline space) is
        // reserved unless overflow hides it (a textarea scrolls its contents: visible behaves as auto), and the one of the inline
        // axis only when overflow forces it (the lines wrap)
        var (average, _) = GetCharacterWidths(style);
        var (inlineScrollbar, blockScrollbar) = GetTextAreaScrollbarSpace(style, scrollbarThickness);
        return new FormControlSize((textArea.Cols * average) + inlineScrollbar, (textArea.Rows * lineHeight) + blockScrollbar, FormControlBaseline.FirstLine);
    }

    /// <summary>
    /// Returns the space of the classic scrollbars in the natural size of a <c>textarea</c> (see <see cref="GetTextAreaSize"/>):
    /// the inline space of the scrollbar of the block axis, and the block space of the scrollbar of the inline axis. The
    /// scrollbars that the layout displays take their space from it (see <see cref="ReplacedFormattingContext"/>).
    /// </summary>
    internal static (double Inline, double Block) GetTextAreaScrollbarSpace(ComputedStyle style, double scrollbarThickness)
    {
        if (scrollbarThickness <= 0 || IsFieldSizingContent(style))
            return default;

        var layout = style.Layout;
        var (inlineOverflow, blockOverflow) = layout.IsHorizontalWritingMode ? (layout.OverflowY, layout.OverflowX) : (layout.OverflowX, layout.OverflowY);
        return (inlineOverflow is OverflowKind.Hidden or OverflowKind.Clip ? 0 : scrollbarThickness, blockOverflow == OverflowKind.Scroll ? scrollbarThickness : 0);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2</summary>
    private static FormControlSize GetSelectSize(HtmlSelectElement select, ComputedStyle style, bool fieldSizingContent, double scrollbarThickness)
    {
        var lineHeight = GetLineHeight(style);
        var options = select.GetListOfOptions();
        var isListBox = select.IsMultiple || select.DisplaySize > 1;

        // "The width of the select's labels is the wider of the width necessary to render the widest optgroup, and the width
        // necessary to render the widest option element in the element's list of options". The options and the groups of a list
        // box that generate no box are not rendered
        double labelsWidth = 0;
        HtmlOptionElement? selected = null;
        var displayedOptions = 0;
        foreach (var option in options)
        {
            if (selected is null && option.Selected)
            {
                selected = option;
            }

            if (isListBox && !IsDisplayedInListBox(option))
                continue;

            displayedOptions++;
            labelsWidth = Math.Max(labelsWidth, style.MeasureText(GetRenderedText(select, style, option.GetLabel(includeAltText: true))));
        }

        for (var child = select.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child is HtmlOptGroupElement group && group.GetAttributeValue("label") is { Length: > 0 } label && (!isListBox || !IsDisplayNone(group)))
            {
                labelsWidth = Math.Max(labelsWidth, style.MeasureText(GetRenderedText(select, style, label)));
            }
        }

        if (isListBox)
        {
            // "If the 'field-sizing' property on the element has a computed value of 'content', return the height necessary to
            // contain all rows for items" (none without item)
            var rows = fieldSizingContent ? displayedOptions : GetListBoxRows(select);
            return new FormControlSize(labelsWidth + scrollbarThickness, rows * lineHeight, FormControlBaseline.FirstLine);
        }

        // "If the 'field-sizing' property on the element has a computed value of 'content', the inline size of the intrinsic size
        // depends on the shown text"
        if (fieldSizingContent)
        {
            labelsWidth = selected is null ? 0 : style.MeasureText(GetRenderedText(select, style, selected.GetLabel(includeAltText: true)));
        }

        // The drop-down box has an arrow button after the label (1em, as the picker button of the date and time fields)
        return new FormControlSize(labelsWidth + style.FontSize, lineHeight, FormControlBaseline.CenteredLine);
    }

    /// <summary>The natural size of a drop-down box without label (see <see cref="GetSelectSize"/>): its arrow button and one line, in the coordinates of the element.</summary>
    internal static (double InlineSize, double BlockSize) GetEmptyDropDownBoxSize(ComputedStyle style) => (style.FontSize, GetLineHeight(style));

    /// <summary>
    /// The natural size of a list box without option (see <see cref="GetSelectSize"/>): its scrollbar and its rows (none with
    /// <c>field-sizing: content</c>), in the coordinates of the element.
    /// </summary>
    internal static (double InlineSize, double BlockSize) GetEmptyListBoxSize(HtmlSelectElement select, ComputedStyle style, double scrollbarThickness)
        => (scrollbarThickness, IsFieldSizingContent(style) ? 0 : GetListBoxRows(select) * GetLineHeight(style));

    /// <summary>The number of rows of a list box: its display size, 4 by default (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2).</summary>
    private static long GetListBoxRows(HtmlSelectElement select) => select.DisplaySize > 0 ? select.DisplaySize : 4;

    /// <summary>
    /// Whether an option of a list box is rendered: as in the browsers, where the options of a list box are boxes,
    /// <c>display: none</c> on the option or on its group removes it from the list (https://drafts.csswg.org/css-display/#valdef-display-none).
    /// </summary>
    internal static bool IsDisplayedInListBox(HtmlOptionElement option)
        => !IsDisplayNone(option) && (option.ParentElement is not HtmlOptGroupElement group || !IsDisplayNone(group));

    private static bool IsDisplayNone(Element element) => element.NodeDocument.GetStyleEngine().GetStyle(element).Layout.Display.IsNone;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/rendering.html#converting-a-character-width-to-pixels: "(size-1)×avg + max, where
    /// size is the character width to convert, avg is the average character width of the primary font for the element [...]
    /// and max is the maximum character width of that same font".
    /// </summary>
    private static double ConvertCharacterWidthToPixels(ComputedStyle style, long size)
    {
        var (average, maximum) = GetCharacterWidths(style);
        return ((size - 1) * average) + maximum;
    }

    /// <summary>
    /// Returns the average character width of the primary font (<c>OS/2.xAvgCharWidth</c>, else the advance of the digit
    /// zero) and its maximum character width (the width of the bounding box of the glyphs, at least the average width). As in
    /// Chrome, the widths of the OS/2 table are rounded to integers.
    /// </summary>
    private static (double Average, double Maximum) GetCharacterWidths(ComputedStyle style)
    {
        var metrics = style.PrimaryFontMetrics;
        var font = style.Fonts.PrimaryFont.Font;
        if (font.AverageCharWidth <= 0)
        {
            var zero = style.MeasureText("0");
            return (zero, zero);
        }

        var average = Math.Round(font.AverageCharWidth * metrics.Scale, MidpointRounding.AwayFromZero);
        return (average, Math.Max(average, Math.Round(font.MaxCharWidth * metrics.Scale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// The height of the line of a single-line text field (zoomed): its used line height, at least the normal line height (as in
    /// the browsers, a smaller line height does not shrink the field).
    /// </summary>
    internal static double GetTextFieldLineHeight(ComputedStyle style)
    {
        var layout = style.Layout;
        return Math.Max(layout.UsedLineHeight, style.Fonts.PrimaryFont.GetMetrics(layout.FontSize).NormalLineHeight);
    }

    /// <summary>Whether an input element is a single-line text field (its line is at least as tall as the normal line height).</summary>
    internal static bool IsTextField(HtmlInputElement input) => input.InputType is not (InputType.Checkbox or InputType.Radio or InputType.Range or InputType.Color or InputType.Submit or InputType.Reset or InputType.Button or InputType.Image or InputType.File or InputType.Hidden);

    /// <summary>The used line height, in the coordinates of the element (not zoomed).</summary>
    private static double GetLineHeight(ComputedStyle style) => style.Layout.UsedLineHeight / style.Layout.EffectiveZoom;
}
