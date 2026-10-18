using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The native appearance of the form controls (https://html.spec.whatwg.org/multipage/rendering.html#widgets): a simple and
// stable look, the same on every platform. The controls are replaced boxes (see FormControlSizing): their contents (the label
// of a button, the selected option of a drop-down box, the value of a date and time field...) are painted in their content box,
// except the value of the text fields and of the textarea elements, which is laid out in their inner editor (see TextControlLayout).
internal sealed partial class Painter
{
    /// <summary>The color of the frames of the native controls (the borders of the checkboxes, the tracks).</summary>
    private static readonly DisplayColor ControlFrameColor = DisplayColor.FromRgb(0x76, 0x76, 0x76);

    /// <summary>The color of the checked state and of the values of the native controls (the used value of <c>accent-color: auto</c>).</summary>
    private static readonly DisplayColor ControlAccentColor = DisplayColor.FromRgb(0x00, 0x75, 0xFF);

    /// <summary>
    /// Returns the accent color of a widget (https://drafts.csswg.org/css-ui-4/#widget-accent): the computed <c>accent-color</c>
    /// (applied to the checkboxes, the radio buttons, the sliders and the progress bars), or the color of the user agent for <c>auto</c>.
    /// "If the color supplied is partially or fully transparent, the user agent must first pre-compose it over the Canvas system
    /// color" (of the used color scheme of the control).
    /// </summary>
    private static DisplayColor GetAccentColor(ComputedStyle style)
    {
        if (style.GetComputedValue("accent-color") is "auto" or "" || DisplayColor.Parse(style.GetResolvedValue("accent-color")) is not { } accent)
            return ControlAccentColor;

        if (accent.IsOpaque)
            return accent;

        var canvas = DisplayColor.Parse(CssValueComputer.GetSystemColor(style, "canvas")) ?? DisplayColor.White;
        var alpha = Math.Clamp(accent.A, 0, 1);
        return new DisplayColor((accent.R * alpha) + (canvas.R * (1 - alpha)), (accent.G * alpha) + (canvas.G * (1 - alpha)), (accent.B * alpha) + (canvas.B * (1 - alpha)), 1);
    }

    /// <summary>
    /// Returns the color of the marks drawn on the accent color (the check mark of a checkbox): white, or black when the accent
    /// color is light ("the UA should [...] ensure legibility", e.g. the contrast of the check mark).
    /// </summary>
    private static DisplayColor GetAccentContrastColor(DisplayColor accent)
        => (0.2126 * accent.R) + (0.7152 * accent.G) + (0.0722 * accent.B) > 0.7 ? DisplayColor.Black : DisplayColor.White;

    /// <summary>The color of the empty part of the tracks of the native controls.</summary>
    private static readonly DisplayColor ControlTrackColor = DisplayColor.FromRgb(0xEF, 0xEF, 0xEF);

    /// <summary>
    /// Paints the contents of a form control: the text of its value and, with the native appearance
    /// (<c>appearance</c> other than <c>none</c>), the glyph of a checkbox or radio button, the track of a slider, of a progress
    /// bar or of a meter, the swatch of a color well and the arrow of a drop-down box.
    /// </summary>
    /// <remarks>
    /// In a vertical writing mode (https://html.spec.whatwg.org/multipage/rendering.html#form-controls: the sizes of the controls
    /// are logical), the contents are painted as in a horizontal one, in the content box rotated so that its line-over side is at
    /// the top: a quarter turn clockwise (<c>vertical-rl</c>, <c>vertical-lr</c>, <c>sideways-rl</c>) or counter-clockwise
    /// (<c>sideways-lr</c>). The lines of text and the tracks run along the inline axis from the inline-start side; the text is
    /// set sideways (<c>text-orientation: upright</c> and the upright characters of <c>mixed</c> are not typeset upright). The
    /// rows of a list box are not painted here: they are the boxes of its options (see <see cref="FormControlContents"/>).
    /// </remarks>
    private void PaintFormControl(BoxFragment fragment, PhysicalOffset offset)
    {
        var element = fragment.Box.Element;
        var style = fragment.Box.Style;
        var native = FormControlContents.HasNativeAppearance(element!, style);
        if (native && FormControlContents.HasNativeFrame(element!) && (element is not HtmlSelectElement || style.GetComputedValue("appearance") != "menulist-button"))
        {
            PaintNativeFrame(element!, GetSnappedBorderBox(fragment, offset), style.Layout.EffectiveZoom);
        }

        // The value and the placeholder of a text control (a textarea or a text field, see TextControlLayout) are the lines of
        // its inner editor and of its ::placeholder box: their fragments are the children of the fragment of the control, painted
        // in the painting order as the contents of the other boxes (in the clips of the control: the scrollport of a textarea,
        // which scrolls them; the overflow clip edge of a text field, whose inner editor clips and scrolls its line)
        if (TextControlLayout.HasInnerEditor(element) || FormControlContents.HasContentsFragment(fragment))
            return;

        var content = SnapToDevicePixels(fragment.ContentBoxRect.Translate(offset));
        var writingDirection = fragment.WritingDirection;
        var padding = fragment.Padding;
        if (writingDirection.IsHorizontal || (element is HtmlInputElement { InputType: InputType.Checkbox or InputType.Radio or InputType.Color or InputType.Hidden } && !FormControlContents.IsSwitch(element)))
        {
            PaintFormControlContents(element, style, content, padding, native);
            return;
        }

        if (content.IsEmpty)
            return;

        // The rotated content box is at the origin: its top left corner is the top right (clockwise) or the bottom left corner
        // of the content box
        var clockwise = writingDirection.LineOver == PhysicalSide.Right;
        var matrix = clockwise
            ? DisplayMatrix.Translation(content.Right, content.Y).PreConcat(DisplayMatrix.Rotation(90))
            : DisplayMatrix.Translation(content.X, content.Bottom).PreConcat(DisplayMatrix.Rotation(-90));
        // The paddings around the rotated content box: its top side is the right (clockwise) or the left side of the content box
        var rotatedPadding = clockwise
            ? new Sides<double>(padding.Right, padding.Bottom, padding.Left, padding.Top)
            : new Sides<double>(padding.Left, padding.Top, padding.Right, padding.Bottom);
        _builder.Save();
        _builder.Add(new TransformItem(matrix));
        PaintFormControlContents(element, style, new PhysicalRect(0, 0, content.Height, content.Width), rotatedPadding, native);
        _builder.Restore();
    }

    /// <summary>
    /// Paints the native frame of a text field, of a <c>textarea</c> or of a drop-down box (see <see cref="FormControlContents.HasNativeFrame"/>):
    /// a thin rounded frame along the edges of its border box, over its background (the user agent style sheet of the
    /// specification gives the controls no border: the frame takes no space). A devolved widget has no frame, nor a drop-down
    /// box with <c>appearance: menulist-button</c> (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2:
    /// "CSS properties such as 'color', 'background-color', and 'border' should not be disregarded", its borders are the ones of
    /// CSS).
    /// </summary>
    private void PaintNativeFrame(Element element, PhysicalRect border, double zoom)
    {
        if (border.Width <= 2 * zoom || border.Height <= 2 * zoom)
            return;

        var radius = 2 * zoom;
        var frame = new RoundedRect(border, new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius)));
        _builder.Add(new DrawRingItem(frame, frame.Inset(Uniform(zoom)), new DisplayPaint(ControlFrameColor)) { Source = element });
    }

    /// <summary>
    /// Paints the contents of a form control in its content box, with horizontal lines (see <see cref="PaintFormControl"/>).
    /// <paramref name="padding"/> is the padding around the content box.
    /// </summary>
    private void PaintFormControlContents(Element? element, ComputedStyle style, PhysicalRect content, Sides<double> padding, bool native)
    {
        var linePadding = (Over: padding.Top, Under: padding.Bottom);
        var zoom = style.Layout.EffectiveZoom;
        var rtl = style.Layout.Direction == Direction.Rtl;
        switch (element)
        {
            case HtmlInputElement input:
                switch (input.InputType)
                {
                    case InputType.Checkbox when native && FormControlContents.IsSwitch(input):
                        PaintSwitch(input, content, zoom, rtl, GetAccentColor(style));
                        return;

                    case InputType.Checkbox or InputType.Radio:
                        if (native)
                        {
                            PaintCheckbox(input, content, zoom, GetAccentColor(style));
                        }

                        return;

                    case InputType.Range:
                        // (without the native appearance, the parts of a slider are boxes, see FormControlContents)
                        if (native)
                        {
                            PaintSlider(input, content, FormControlContents.GetSliderProgress(input), zoom, rtl, GetAccentColor(style));
                        }

                        return;

                    case InputType.Color:
                        if (native)
                        {
                            var swatch = content.Contract(new Sides<double>(2 * zoom, 2 * zoom, 2 * zoom, 2 * zoom));
                            var color = DisplayColor.Parse(input.Value) ?? DisplayColor.Black;
                            _builder.Add(new DrawRectItem(swatch, new DisplayPaint(color)) { Source = input });
                            BorderPainter.Paint(_builder, new RoundedRect(swatch, default), Uniform(zoom), new Sides<BorderStyle>(BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid, BorderStyle.Solid), new Sides<DisplayColor>(ControlFrameColor, ControlFrameColor, ControlFrameColor, ControlFrameColor), input, _devicePixelRatio);
                        }

                        return;

                    case InputType.Submit or InputType.Reset or InputType.Button:
                        PaintButtonLabel(input, style, content, FormControlSizing.GetButtonLabelLines(input, style), GetColor(style, "color"));
                        return;

                    case InputType.Image:
                        PaintControlLine(input, style, content, input.GetImageButtonLabel(), GetColor(style, "color"));
                        return;

                    case InputType.Hidden:
                        return;

                    default:
                    {
                        // The text of a date and time control (the value of a text field is laid out in its inner editor, see
                        // PaintFormControl) is clipped at the content edge in the inline axis, and at the overflow clip
                        // edge of the control in the block axis: its padding edge ("input { overflow: clip; overflow-clip-margin:
                        // 0 }"), so that the text that is taller than the content box is not cut
                        var clip = new PhysicalRect(content.X, content.Y - Math.Max(0, linePadding.Over), content.Width, content.Height + Math.Max(0, linePadding.Over) + Math.Max(0, linePadding.Under));
                        var value = input.Value;
                        if (value.Length > 0)
                        {
                            PaintControlLine(input, style, content, value, GetColor(style, "color"), clip);
                        }
                        else if (FormControlSizing.GetEmptyDateTimeText(input.InputType) is { } format)
                        {
                            // The empty fields of a date and time control show their format, as in the browsers
                            PaintControlLine(input, style, content, format, GetColor(style, "color"), clip);
                        }

                        return;
                    }
                }

            case HtmlSelectElement select:
                PaintSelect(select, style, content, native, zoom);
                return;

            case HtmlProgressElement progress:
                // (without the native appearance, the parts of a progress bar and of a meter are boxes, see FormControlContents)
                if (native)
                {
                    PaintBar(progress, content, FormControlContents.GetSliderProgress(progress), ControlTrackColor, GetAccentColor(style), rtl);
                }

                return;

            case HtmlMeterElement meter:
                if (native)
                {
                    PaintBar(meter, content, FormControlContents.GetSliderProgress(meter), ControlTrackColor, DisplayColor.FromRgb(0x10, 0x7C, 0x10), rtl);
                }

                return;
        }
    }

    /// <summary>Paints a checkbox (a square with a check mark) or a radio button (a circle with a dot).</summary>
    private void PaintCheckbox(HtmlInputElement input, PhysicalRect content, double zoom, DisplayColor accent)
    {
        var size = Math.Min(content.Width, content.Height);
        if (size <= 0)
            return;

        var box = new PhysicalRect(content.X + ((content.Width - size) / 2), content.Y + ((content.Height - size) / 2), size, size);
        var radio = input.InputType == InputType.Radio;
        var radius = radio ? size / 2 : 2 * zoom;
        var radii = new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius));
        var shape = new RoundedRect(box, radii);
        var checkedState = input.Checked;
        var indeterminate = !radio && input.Indeterminate;
        var filled = !radio && (checkedState || indeterminate);

        // The box that is not checked has the Field color of the used color scheme of the control (white, or dark)
        var field = input.NodeDocument.GetStyleEngine().GetStyle(input) is { } style ? DisplayColor.Parse(CssValueComputer.GetSystemColor(style, "field")) ?? DisplayColor.White : DisplayColor.White;
        _builder.Add(new DrawRoundedRectItem(shape, new DisplayPaint(filled ? accent : field)) { Source = input });
        if (!filled)
        {
            _builder.Add(new DrawRingItem(shape, shape.Inset(Uniform(zoom)), new DisplayPaint(ControlFrameColor)) { Source = input });
        }

        if (radio && checkedState)
        {
            var ring = shape.Inset(Uniform(zoom));
            _builder.Add(new DrawRingItem(shape, ring, new DisplayPaint(accent)) { Source = input });
            var dot = box.Contract(Uniform(size * 0.25));
            var dotRadius = dot.Width / 2;
            _builder.Add(new DrawRoundedRectItem(new RoundedRect(dot, new BorderRadii((dotRadius, dotRadius), (dotRadius, dotRadius), (dotRadius, dotRadius), (dotRadius, dotRadius))), new DisplayPaint(accent)) { Source = input });
        }
        else if (indeterminate)
        {
            _builder.Add(new DrawRectItem(new PhysicalRect(box.X + (size * 0.25), box.Y + (size * 0.45), size * 0.5, size * 0.1), new DisplayPaint(GetAccentContrastColor(accent))) { Source = input });
        }
        else if (checkedState)
        {
            var check = new DisplayPath();
            check.MoveTo(box.X + (size * 0.22), box.Y + (size * 0.52));
            check.LineTo(box.X + (size * 0.42), box.Y + (size * 0.72));
            check.LineTo(box.X + (size * 0.78), box.Y + (size * 0.3));
            _builder.Add(new DrawPathItem(check, DisplayPaint.ForStroke(GetAccentContrastColor(accent), new StrokeStyle(Math.Max(1, size * 0.14), LineCap.Round, LineJoin.Round))) { Source = input });
        }
    }

    /// <summary>
    /// Paints a switch (see <see cref="FormControlContents.IsSwitch"/>): a rounded track, filled with the accent color when it is
    /// checked, and a round thumb at its inline-start side (the right side when <paramref name="rtl"/>), or at its inline-end side
    /// when it is checked. The indeterminate state does not apply to a switch.
    /// </summary>
    private void PaintSwitch(HtmlInputElement input, PhysicalRect content, double zoom, bool rtl, DisplayColor accent)
    {
        if (content.IsEmpty)
            return;

        var radius = Math.Min(content.Width, content.Height) / 2;
        var track = new RoundedRect(content, new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius)));
        var checkedState = input.Checked;
        var field = input.NodeDocument.GetStyleEngine().GetStyle(input) is { } style ? DisplayColor.Parse(CssValueComputer.GetSystemColor(style, "field")) ?? DisplayColor.White : DisplayColor.White;
        _builder.Add(new DrawRoundedRectItem(track, new DisplayPaint(checkedState ? accent : field)) { Source = input });
        if (!checkedState)
        {
            _builder.Add(new DrawRingItem(track, track.Inset(Uniform(zoom)), new DisplayPaint(ControlFrameColor)) { Source = input });
        }

        var inset = Math.Min(2 * zoom, radius / 2);
        var diameter = Math.Max(0, (2 * radius) - (2 * inset));
        var atEnd = checkedState != rtl;
        var x = atEnd ? content.Right - inset - diameter : content.X + inset;
        var thumb = new PhysicalRect(x, content.Y + ((content.Height - diameter) / 2), diameter, diameter);
        var thumbRadius = diameter / 2;
        _builder.Add(new DrawRoundedRectItem(new RoundedRect(thumb, new BorderRadii((thumbRadius, thumbRadius), (thumbRadius, thumbRadius), (thumbRadius, thumbRadius), (thumbRadius, thumbRadius))), new DisplayPaint(checkedState ? GetAccentContrastColor(accent) : ControlFrameColor)) { Source = input });
    }

    /// <summary>
    /// Paints a slider: a track with the part before the thumb in the accent color, and a round thumb. The values grow from the
    /// inline-start side: from the right when <paramref name="rtl"/>.
    /// </summary>
    private void PaintSlider(HtmlInputElement input, PhysicalRect content, double fraction, double zoom, bool rtl, DisplayColor accent)
    {
        if (content.IsEmpty)
            return;

        var thumb = Math.Min(content.Height, 16 * zoom);
        var trackHeight = 4 * zoom;
        var trackY = content.Y + ((content.Height - trackHeight) / 2);
        var left = content.X + (thumb / 2);
        var length = Math.Max(0, content.Width - thumb);
        var position = left + (length * (rtl ? 1 - fraction : fraction));
        var radius = trackHeight / 2;
        var trackRadii = new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius));
        var filled = rtl ? new PhysicalRect(position, trackY, content.Right - position, trackHeight) : new PhysicalRect(content.X, trackY, position - content.X, trackHeight);
        _builder.Add(new DrawRoundedRectItem(new RoundedRect(new PhysicalRect(content.X, trackY, content.Width, trackHeight), trackRadii), new DisplayPaint(ControlTrackColor)) { Source = input });
        _builder.Add(new DrawRoundedRectItem(new RoundedRect(filled, trackRadii), new DisplayPaint(accent)) { Source = input });
        PaintSliderThumb(input, new PhysicalRect(position - (thumb / 2), content.Y + ((content.Height - thumb) / 2), thumb, thumb), accent);
    }

    /// <summary>Paints the native appearance of the thumb of a slider: a disc of the accent color.</summary>
    private void PaintSliderThumb(Element input, PhysicalRect thumb, DisplayColor accent)
    {
        var radius = Math.Min(thumb.Width, thumb.Height) / 2;
        _builder.Add(new DrawRoundedRectItem(new RoundedRect(thumb, new BorderRadii((radius, radius), (radius, radius), (radius, radius), (radius, radius))), new DisplayPaint(accent)) { Source = input });
    }

    /// <summary>
    /// Paints the native appearance of a part of a form control that is a box (see <see cref="FormControlContents"/>): the
    /// thumb of a slider with primitive appearance, which, as in the browsers, has its own appearance (<c>auto</c> in the user
    /// agent style sheet) until the author styles its backgrounds or its borders.
    /// </summary>
    private void PaintNativePart(BoxFragment fragment, PhysicalOffset offset)
    {
        var box = fragment.Box;
        if (box.PseudoElement != FormControlContents.SliderThumb || box.Element is not HtmlInputElement input || !FormControlContents.HasNativePartAppearance(box.Style))
            return;

        PaintSliderThumb(input, SnapToDevicePixels(fragment.BorderBoxRect.Translate(offset)), GetAccentColor(box.Style));
    }

    /// <summary>Paints a progress bar or a meter: a track filled up to a fraction, from the inline-start side (the right when <paramref name="rtl"/>).</summary>
    private void PaintBar(Element element, PhysicalRect content, double fraction, DisplayColor trackColor, DisplayColor color, bool rtl)
    {
        if (content.IsEmpty)
            return;

        _builder.Add(new DrawRectItem(content, new DisplayPaint(trackColor)) { Source = element });
        var horizontal = content.Width >= content.Height;
        var filledWidth = content.Width * fraction;
        var filled = horizontal
            ? new PhysicalRect(rtl ? content.Right - filledWidth : content.X, content.Y, filledWidth, content.Height)
            : new PhysicalRect(content.X, content.Bottom - (content.Height * fraction), content.Width, content.Height * fraction);
        if (!filled.IsEmpty)
        {
            _builder.Add(new DrawRectItem(filled, new DisplayPaint(color)) { Source = element });
        }
    }

    /// <summary>Paints a select element rendered as a drop-down box: the label of its selected option and an arrow.</summary>
    private void PaintSelect(HtmlSelectElement select, ComputedStyle style, PhysicalRect content, bool native, double zoom)
    {
        var options = select.GetListOfOptions();
        var color = GetColor(style, "color");
        // The rows of a list box are the boxes of its options (see FormControlContents)
        if (!StyleEngine.IsDropDownBox(select))
            return;

        HtmlOptionElement? selected = null;
        foreach (var option in options)
        {
            if (option.Selectedness)
            {
                selected = option;
                break;
            }
        }

        // The arrow (the "drop-down button") takes the end of the content box: with the native appearance, and also in the devolved
        // state, whose appearance "is that of a drop-down box, including a "drop-down button"", as with appearance: menulist-button
        // (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2)
        var arrowSize = native || (style.GetComputedValue("appearance") is not ("none" or "") && !StyleEngine.HasControlBaseAppearance(style)) ? Math.Min(content.Height, style.Layout.FontSize) : 0;
        var textArea = content with { Width = Math.Max(0, content.Width - arrowSize) };
        if (selected is not null)
        {
            PaintControlLine(select, style, textArea, GetOptionLabel(selected), color);
        }

        if (arrowSize > 0)
        {
            var center = new PhysicalOffset(content.Right - (arrowSize / 2), content.Y + (content.Height / 2));
            var half = arrowSize * 0.25;
            var arrow = new DisplayPath();
            arrow.MoveTo(center.X - half, center.Y - (half / 2));
            arrow.LineTo(center.X, center.Y + (half / 2));
            arrow.LineTo(center.X + half, center.Y - (half / 2));
            _builder.Add(new DrawPathItem(arrow, DisplayPaint.ForStroke(color, new StrokeStyle(Math.Max(1, zoom * 1.5), LineCap.Round, LineJoin.Round))) { Source = select });
        }
    }

    private static string GetOptionLabel(HtmlOptionElement option) => option.GetAttributeValue("label") is { Length: > 0 } label ? label : option.Text;

    /// <summary>
    /// Paints a line of text of a control (a text field, a button, a drop-down box) vertically centered in its content box, clipped
    /// to it (or to <paramref name="clip"/>).
    /// </summary>
    private void PaintControlLine(Element element, ComputedStyle style, PhysicalRect content, string text, DisplayColor color, PhysicalRect? clip = null)
    {
        if (text.Length == 0 || color.IsTransparent || content.Width <= 0 || (clip ?? content).IsEmpty)
            return;

        _builder.Save();
        _builder.Add(new ClipRectItem(clip ?? content));
        PaintControlText(element, style, content, FormControlSizing.GetRenderedText(element, style, text), color, centered: true);
        _builder.Restore();
    }

    /// <summary>
    /// Paints the lines of the label of a button (<see cref="FormControlSizing.GetButtonLabelLines"/>: the line feeds of the
    /// value break the lines, which do not wrap), vertically centered as a group in its content box, clipped to it.
    /// </summary>
    private void PaintButtonLabel(Element element, ComputedStyle style, PhysicalRect content, string[] lines, DisplayColor color)
    {
        if (color.IsTransparent || content.IsEmpty)
            return;

        if (lines.Length == 1)
        {
            if (lines[0].Length > 0)
            {
                _builder.Save();
                _builder.Add(new ClipRectItem(content));
                PaintControlText(element, style, content, lines[0], color, centered: true);
                _builder.Restore();
            }

            return;
        }

        var lineHeight = style.Layout.UsedLineHeight;
        var y = content.Y + ((content.Height - (lines.Length * lineHeight)) / 2);
        _builder.Save();
        _builder.Add(new ClipRectItem(content));
        for (var i = 0; i < lines.Length; i++)
        {
            PaintControlText(element, style, new PhysicalRect(content.X, y + (i * lineHeight), content.Width, lineHeight), lines[i], color, centered: false, firstLine: i == 0);
        }

        _builder.Restore();
    }

    private static TextShapingOptions GetControlShapingOptions(ComputedStyle style) => TextShapingOptions.FromStyle(style) with { FontSize = style.Layout.FontSize, IsRightToLeft = false };

    /// <summary>
    /// Paints a line of text of a control in a rectangle: aligned with <c>text-align</c> (start, center, end), vertically centered
    /// (<paramref name="centered"/>) or on the baseline of a line box at the top of the rectangle.
    /// </summary>
    private void PaintControlText(Element element, ComputedStyle style, PhysicalRect area, string text, DisplayColor color, bool centered, bool firstLine = true)
    {
        if (text.Length == 0)
            return;

        var layout = style.Layout;
        var options = GetControlShapingOptions(style);
        var rtl = layout.Direction == Direction.Rtl;

        // The bidi algorithm (https://www.unicode.org/reports/tr9/) applies to the line when its paragraph is right-to-left or
        // when it has characters of the right-to-left scripts or bidi controls: its level runs are shaped in their direction
        // and drawn in visual order. The paragraph level is the direction of the control, or the one of rules P2 and P3 with
        // unicode-bidi: plaintext (dir=auto), which then gives the start edge of the line
        List<BidiRun>? bidiRuns = null;
        if (rtl || layout.UnicodeBidi == UnicodeBidi.Plaintext || LineEllipsis.NeedsBidi(text))
        {
            var resolver = new BidiResolver();
            resolver.Resolve(text, BidiControls.GetParagraphDirection(layout.UnicodeBidi, rtl));
            var levels = new byte[text.Length];
            resolver.GetLineLevels(0, text.Length, levels);
            if (layout.UnicodeBidi == UnicodeBidi.Plaintext && resolver.Paragraphs.Count > 0)
            {
                rtl = resolver.Paragraphs[0].IsRightToLeft;
            }

            bidiRuns = [];
            BidiResolver.GetVisualRuns(levels, bidiRuns);
            if (bidiRuns is [{ IsRightToLeft: false }])
            {
                bidiRuns = null;
            }
        }

        ShapedText[]? shapedRuns = null;
        ShapedText shaped;
        double width;
        if (bidiRuns is null)
        {
            shaped = TextShaper.Shape(text, style.Fonts, options);
            if (shaped.Runs.Length == 0)
                return;

            width = shaped.Width;
        }
        else
        {
            shaped = default!;
            shapedRuns = new ShapedText[bidiRuns.Count];
            width = 0;
            for (var i = 0; i < shapedRuns.Length; i++)
            {
                var run = bidiRuns[i];
                var characters = text.AsSpan(run.Start, run.End - run.Start);
                shapedRuns[i] = TextShaper.Shape(run.IsRightToLeft ? LineEllipsis.Mirror(characters) : characters, style.Fonts, options with { IsRightToLeft = run.IsRightToLeft });
                width += shapedRuns[i].Width;
            }
        }

        var metrics = style.Fonts.PrimaryFont.GetMetrics(layout.FontSize);
        var lineHeight = element is HtmlInputElement input && FormControlSizing.IsTextField(input) ? FormControlSizing.GetTextFieldLineHeight(style) : layout.UsedLineHeight;
        var top = centered ? area.Y + ((area.Height - lineHeight) / 2) : area.Y;

        // https://drafts.csswg.org/css-text-3/#text-indent-property: the first line of the control is indented
        if (firstLine && LengthPercentage.TryParse(style.GetComputedValue("text-indent").Split(' ')[0], layout.EffectiveZoom, nonNegative: false, out var indent))
        {
            var indentWidth = indent.Resolve(area.Width);
            area = layout.Direction == Direction.Rtl ? area with { Width = area.Width - indentWidth } : new PhysicalRect(area.X + indentWidth, area.Y, area.Width - indentWidth, area.Height);
        }

        var baseline = SnapToDevicePixels(top + Math.Floor((lineHeight - (metrics.Ascent + metrics.Descent)) / 2) + metrics.Ascent);

        // https://drafts.csswg.org/css-text-4/#text-align-property: "If (after justification) the inline contents of a line box
        // are too long to fit within it, then the contents are start-aligned"
        var x = (width > area.Width ? TextAlign.Start : layout.Text.TextAlign) switch
        {
            TextAlign.Center => area.X + ((area.Width - width) / 2),
            TextAlign.Right => area.Right - width,
            TextAlign.End => rtl ? area.X : area.Right - width,
            TextAlign.Left => area.X,
            _ => rtl ? area.Right - width : area.X,
        };

        if (shapedRuns is not null)
        {
            // (text-overflow does not elide the lines reordered by the bidi algorithm: they are clipped)
            for (var i = 0; i < shapedRuns.Length; i++)
            {
                DrawControlGlyphs(element, shapedRuns[i], text, x, baseline, color, 0, shapedRuns[i].Glyphs.Length, bidiRuns![i].IsRightToLeft);
                x += shapedRuns[i].Width;
            }

            return;
        }

        var (visibleStart, visibleEnd) = TruncateControlText(element, style, area, shaped, x, baseline, color, options);
        DrawControlGlyphs(element, shaped, text, x, baseline, color, visibleStart, visibleEnd);
    }

    /// <summary>
    /// Draws the glyphs of a shaped text of a control from <paramref name="x"/>, but the glyphs outside of a range. The glyphs of
    /// a right-to-left run are in logical order: they are placed from the right edge of the run.
    /// </summary>
    private void DrawControlGlyphs(Element element, ShapedText shaped, string text, double x, double baseline, DisplayColor color, int start, int end, bool rightToLeft = false)
    {
        var glyphs = shaped.Glyphs;
        var pen = x;
        var right = x + shaped.Width;
        foreach (var run in shaped.Runs)
        {
            var ids = new List<ushort>(run.GlyphCount);
            var positions = new List<PhysicalOffset>(run.GlyphCount);
            for (var i = run.GlyphStart; i < run.GlyphStart + run.GlyphCount; i++)
            {
                var glyph = glyphs[i];
                if (!glyph.IsInvisible && i >= start && i < end)
                {
                    ids.Add(glyph.GlyphId);
                    positions.Add(new PhysicalOffset((rightToLeft ? right - (pen - x) - glyph.Advance : pen) + glyph.OffsetX, baseline + glyph.OffsetY));
                }

                pen += glyph.Advance;
            }

            if (ids.Count > 0)
            {
                _builder.Add(new DrawGlyphsItem(new GlyphRun(DisplayFont.From(run.Font, run.EmSize), [.. ids], [.. positions], text), new DisplayPaint(color)) { Source = element });
            }
        }
    }

    /// <summary>
    /// Applies <c>text-overflow</c> (https://drafts.csswg.org/css-overflow-4/#text-overflow) to the line of the text of an
    /// input element drawn from <paramref name="x"/> (a date and time field, which has no inner editor: its text is painted;
    /// see <see cref="LineTruncator"/> for the lines of the block containers and of the inner editors of the text controls):
    /// the clusters that overflow an edge whose value elides
    /// are hidden to make room for its ellipsis, but the first one, and the ellipsis is drawn next to the remaining text. Returns the
    /// range of the glyphs to draw. As in the browsers, the text of a focused control is clipped ("During user interaction [...],
    /// user agents may treat ellipsis [...] as text-overflow: clip").
    /// </summary>
    private (int Start, int End) TruncateControlText(Element element, ComputedStyle style, PhysicalRect area, ShapedText shaped, double x, double baseline, DisplayColor color, in TextShapingOptions options)
    {
        var glyphs = shaped.Glyphs;
        var textOverflow = style.Layout.Overflow.TextOverflow;
        if (!textOverflow.Elides || element is not (HtmlInputElement or HtmlTextAreaElement) || ReferenceEquals(element.NodeDocument.FocusedArea, element))
            return (0, glyphs.Length);

        const double Epsilon = 1e-6;
        var (left, right) = textOverflow.GetLineSides(style.Layout.Direction == Direction.Rtl);
        var start = 0;
        var end = glyphs.Length;
        if (right.Elides && x + shaped.Width > area.Right + Epsilon)
        {
            var ellipsisText = LineEllipsis.GetText(style, right);
            var ellipsis = TextShaper.Shape(ellipsisText, style.Fonts, options);
            var limit = area.Right - ellipsis.Width;

            // The clusters that end after the limit are hidden, but the first one
            var pen = x;
            var kept = x;
            for (var i = 0; i < glyphs.Length; i++)
            {
                var clusterEnd = i + 1;
                while (clusterEnd < glyphs.Length && (glyphs[clusterEnd].Flags & ShapedGlyphFlags.ClusterStart) == ShapedGlyphFlags.None)
                {
                    clusterEnd++;
                }

                var next = pen;
                for (var j = i; j < clusterEnd; j++)
                {
                    next += glyphs[j].Advance;
                }

                if (i > 0 && next > limit + Epsilon)
                {
                    end = i;
                    break;
                }

                pen = next;
                kept = next;
                i = clusterEnd - 1;
            }

            DrawControlGlyphs(element, ellipsis, ellipsisText, kept, baseline, color, 0, ellipsis.Glyphs.Length);
        }

        if (left.Elides && x < area.X - Epsilon)
        {
            var ellipsisText = LineEllipsis.GetText(style, left);
            var ellipsis = TextShaper.Shape(ellipsisText, style.Fonts, options);
            var limit = area.X + ellipsis.Width;

            // The clusters that start before the limit are hidden, but the last one
            var pen = x;
            var lastStart = 0;
            for (var i = 0; i < glyphs.Length; i++)
            {
                if ((glyphs[i].Flags & ShapedGlyphFlags.ClusterStart) != ShapedGlyphFlags.None)
                {
                    lastStart = i;
                    if (pen >= limit - Epsilon)
                        break;
                }

                pen += glyphs[i].Advance;
            }

            start = Math.Min(lastStart, Math.Max(0, end - 1));
            var startPen = x;
            for (var i = 0; i < start; i++)
            {
                startPen += glyphs[i].Advance;
            }

            DrawControlGlyphs(element, ellipsis, ellipsisText, startPen - ellipsis.Width, baseline, color, 0, ellipsis.Glyphs.Length);
        }

        return (start, end);
    }
}
