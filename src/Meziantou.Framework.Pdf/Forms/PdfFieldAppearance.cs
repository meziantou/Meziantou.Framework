using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Writes the normal appearances of the widgets of the text fields and the choice fields from their values (ISO 32000-2,
/// 12.7.4.3): the background and the border of the appearance characteristics (<c>/MK</c>), then the text in the font,
/// size and color of the default appearance (<c>/DA</c>), between <c>/Tx BMC</c> and <c>EMC</c>.
/// </summary>
/// <remarks>
/// The text is drawn with the standard 14 font that matches the font of the default appearance (Helvetica, Times,
/// Courier, their styles, Symbol, ZapfDingbats), Helvetica for another font: the characters that font cannot show are
/// replaced with a question mark.
/// </remarks>
internal static class PdfFieldAppearance
{
    private const double DefaultFontSize = 12;
    private const double MinimumFontSize = 4;

    private static readonly PdfName BGName = PdfName.Create("BG");
    private static readonly PdfName BCName = PdfName.Create("BC");
    private static readonly PdfName BSName = PdfName.Create("BS");
    private static readonly PdfName WName = PdfName.Create("W");
    private static readonly PdfName RName = PdfName.Create("R");
    private static readonly PdfName TIName = PdfName.Create("TI");
    private static readonly PdfName DRName = PdfName.Create("DR");

    /// <summary>Gets the on states of the widgets of a button: the names of their normal appearances other than <c>Off</c>.</summary>
    public static IEnumerable<string> GetOnStates(PdfFormField field)
    {
        foreach (var (widget, _) in field.GetWidgetDictionaries())
        {
            if (widget.Get<PdfDictionary>(PdfFormField.APName)?.Get<PdfDictionary>(PdfNames.N) is { } states)
            {
                foreach (var key in states.Keys)
                {
                    if (key != PdfFormField.OffName)
                        yield return key.Value;
                }
            }
        }
    }

    public static bool HasState(PdfDictionary widget, PdfName state) => widget.Get<PdfDictionary>(PdfFormField.APName)?.Get<PdfDictionary>(PdfNames.N)?.ContainsKey(state) ?? false;

    /// <summary>Writes the appearances of the widgets of a text field or a choice field.</summary>
    public static void Update(PdfFormField field)
    {
        foreach (var (widget, _) in field.GetWidgetDictionaries())
        {
            Update(field, widget);
        }
    }

    private static void Update(PdfFormField field, PdfDictionary widget)
    {
        if (!PdfRectangle.TryRead(widget.Get<PdfArray>(PdfNames.Rect), out var rectangle))
            return;

        var document = field.Form.Document;
        var characteristics = widget.Get<PdfDictionary>(PdfFormField.MKName);
        var rotation = (((int)(characteristics?.GetInteger(RName, 0) ?? 0) % 360) + 360) % 360;
        var (width, height) = rotation is 90 or 270 ? (rectangle.Height, rectangle.Width) : (rectangle.Width, rectangle.Height);
        PdfMatrix? matrix = rotation switch
        {
            90 => new PdfMatrix(0, 1, -1, 0, 0, 0),
            180 => new PdfMatrix(-1, 0, 0, -1, 0, 0),
            270 => new PdfMatrix(0, -1, 1, 0, 0, 0),
            _ => null,
        };

        var form = document.CreateForm(new PdfRectangle(0, 0, width, height), matrix);
        var builder = form.CreateContentBuilder();
        var border = characteristics?.Get<PdfArray>(BCName) is { Count: > 0 } ? widget.Get<PdfDictionary>(BSName)?.GetNumber(WName, 1) ?? 1 : 0;
        if (ReadColor(characteristics?.Get<PdfArray>(BGName)) is { } background)
        {
            builder.SetFillColor(background).Rectangle(0, 0, width, height).Fill();
        }

        if (border > 0 && ReadColor(characteristics?.Get<PdfArray>(BCName)) is { } borderColor)
        {
            builder.SetStrokeColor(borderColor).SetLineWidth(border).Rectangle(border / 2, border / 2, width - border, height - border).Stroke();
        }

        var (font, fontSize, color) = ReadDefaultAppearance(field, widget);
        var padding = border + 1;
        var inner = new PdfRectangle(padding, padding, Math.Max(padding, width - padding), Math.Max(padding, height - padding));
        builder.BeginMarkedContent("Tx").SaveGraphicsState().Rectangle(inner).Clip().EndPath().SetFillColor(color);
        var alignment = (field.GetInherited(PdfFormField.QName) ?? field.Form.Dictionary?.GetResolved(PdfFormField.QName)) is PdfInteger { Value: 1 or 2 } q ? (int)q.Value : 0;
        switch (field)
        {
            case PdfTextField text:
                DrawText(builder, text, font, fontSize, inner, alignment);
                break;
            case PdfChoiceField { IsComboBox: true } choice:
                var value = choice.Value;
                var display = choice.Options.FirstOrDefault(option => option.Value == value)?.Text ?? value ?? "";
                DrawLine(builder, Encodable(font, display), font, fontSize, inner, alignment);
                break;
            case PdfChoiceField choice:
                DrawList(builder, choice, font, fontSize, inner, alignment);
                break;
        }

        builder.RestoreGraphicsState().EndMarkedContent().Complete();
        widget[PdfFormField.APName] = new PdfDictionary { [PdfNames.N] = form.Reference };
    }

    private static void DrawText(PdfContentBuilder builder, PdfTextField field, PdfFont font, double fontSize, PdfRectangle box, int alignment)
    {
        var text = field.Value ?? "";
        if (field.IsPassword)
        {
            text = new string('*', text.Length);
        }

        text = Encodable(font, text);
        if (field.IsComb && field.MaxLength is { } cells && cells > 0)
        {
            // One character per cell, centered (12.7.5.3)
            var size = fontSize > 0 ? fontSize : FitHeight(font, box.Height);
            var cellWidth = box.Width / cells;
            var baseline = Baseline(font, size, box);
            builder.BeginText().SetFont(font, size);
            var index = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (index >= cells)
                    break;

                var character = rune.ToString();
                var x = box.Left + (index * cellWidth) + ((cellWidth - font.GetTextWidth(character, size)) / 2);
                builder.SetTextMatrix(PdfMatrix.CreateTranslation(x, baseline)).ShowText(character);
                index++;
            }

            builder.EndText();
            return;
        }

        if (field.IsMultiline)
        {
            var options = new PdfTextBoxOptions { Alignment = alignment switch { 1 => PdfTextAlignment.Center, 2 => PdfTextAlignment.Right, _ => PdfTextAlignment.Left }, LineHeight = 1.15 };
            builder.DrawTextBox(text, font, fontSize > 0 ? fontSize : DefaultFontSize, box, options);
            return;
        }

        DrawLine(builder, text, font, fontSize, box, alignment);
    }

    /// <summary>Draws one line, vertically centered; a font size of 0 fits the text in the box (12.7.4.3).</summary>
    private static void DrawLine(PdfContentBuilder builder, string text, PdfFont font, double fontSize, PdfRectangle box, int alignment)
    {
        if (text.Length == 0)
            return;

        var size = fontSize;
        if (size <= 0)
        {
            size = FitHeight(font, box.Height);
            var textWidth = font.GetTextWidth(text, size);
            if (textWidth > box.Width)
            {
                size = Math.Max(MinimumFontSize, size * box.Width / textWidth);
            }
        }

        var width = font.GetTextWidth(text, size);
        var x = alignment switch
        {
            1 => box.Left + ((box.Width - width) / 2),
            2 => box.Right - width,
            _ => box.Left + 1,
        };
        builder.BeginText().SetFont(font, size).SetTextMatrix(PdfMatrix.CreateTranslation(x, Baseline(font, size, box))).ShowText(text).EndText();
    }

    /// <summary>Draws the options of a list box from its top index (<c>/TI</c>), the chosen ones highlighted.</summary>
    private static void DrawList(PdfContentBuilder builder, PdfChoiceField field, PdfFont font, double fontSize, PdfRectangle box, int alignment)
    {
        var size = fontSize > 0 ? fontSize : DefaultFontSize;
        var lineHeight = size * (font.Ascent - font.Descent) / 1000;
        var options = field.Options;
        var selected = field.SelectedValues.ToHashSet(StringComparer.Ordinal);
        var top = (int)Math.Clamp(field.GetInherited(TIName) is PdfInteger index ? index.Value : 0, 0, Math.Max(0, options.Count - 1));
        for (var i = top; i < options.Count; i++)
        {
            var lineTop = box.Top - ((i - top) * lineHeight);
            if (lineTop <= box.Bottom)
                break;

            var line = new PdfRectangle(box.Left, lineTop - lineHeight, box.Right, lineTop);
            if (selected.Contains(options[i].Value))
            {
                builder.SaveGraphicsState().SetFillRgb(0.6, 0.75, 0.88).Rectangle(line).Fill().RestoreGraphicsState();
            }

            DrawLine(builder, Encodable(font, options[i].Text), font, size, line, alignment);
        }
    }

    private static double FitHeight(PdfFont font, double height) => Math.Clamp(height / ((font.Ascent - font.Descent) / 1000) * 0.9, MinimumFontSize, DefaultFontSize);

    private static double Baseline(PdfFont font, double size, PdfRectangle box) => box.Bottom + ((box.Height - ((font.Ascent - font.Descent) * size / 1000)) / 2) - (font.Descent * size / 1000);

    /// <summary>Replaces the characters the font cannot show with a question mark.</summary>
    private static string Encodable(PdfFont font, string text)
    {
        if (font.CanEncode(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            result.Append(font.CanEncode(value) ? value : "?");
        }

        return result.ToString();
    }

    /// <summary>Reads the font, the size and the color of the default appearance string (<c>/DA</c>, 12.7.4.3).</summary>
    private static (PdfFont Font, double Size, PdfColor Color) ReadDefaultAppearance(PdfFormField field, PdfDictionary widget)
    {
        var appearance = field.GetInherited(PdfFormField.DAName) as PdfString ?? widget.Get<PdfString>(PdfFormField.DAName) ?? field.Form.Dictionary?.Get<PdfString>(PdfFormField.DAName);
        PdfName? fontName = null;
        var size = 0d;
        var color = PdfColor.FromGray(0);
        if (appearance is not null)
        {
            var reader = new ContentStreamReader(appearance.Bytes);
            var operands = new List<PdfObject>();
            try
            {
                while (reader.TryRead(operands, out var name))
                {
                    double Number(int index) => index < operands.Count && operands[index] is PdfNumber number ? Math.Clamp(number.DoubleValue, 0, 1) : 0;
                    switch (name)
                    {
                        case "Tf" when operands.Count >= 2 && operands[0] is PdfName font && operands[1] is PdfNumber fontSize:
                            fontName = font;
                            size = double.IsFinite(fontSize.DoubleValue) ? Math.Clamp(fontSize.DoubleValue, 0, 1000) : 0;
                            break;
                        case "g" when operands.Count == 1:
                            color = PdfColor.FromGray(Number(0));
                            break;
                        case "rg" when operands.Count == 3:
                            color = PdfColor.FromRgb(Number(0), Number(1), Number(2));
                            break;
                        case "k" when operands.Count == 4:
                            color = PdfColor.FromCmyk(Number(0), Number(1), Number(2), Number(3));
                            break;
                    }
                }
            }
            catch (PdfException)
            {
                // A default appearance that cannot be read keeps what was read before
            }
        }

        var resources = widget.Get<PdfDictionary>(DRName) ?? field.Form.Dictionary?.Get<PdfDictionary>(DRName);
        var baseFont = fontName is null ? null : resources?.Get<PdfDictionary>(PdfNames.Font)?.Get<PdfDictionary>(fontName)?.GetName(PdfNames.BaseFont)?.Value;
        return (PdfFont.GetStandardFont(MatchStandardFont(baseFont ?? fontName?.Value ?? "")), size, color);
    }

    /// <summary>Finds the standard font of a font name, such as <c>Helvetica-Bold</c>, <c>Arial,Bold</c>, <c>TimesNewRomanPSMT</c> or the resource names of Acrobat (<c>Helv</c>, <c>TiRo</c>, <c>Cour</c>, <c>ZaDb</c>).</summary>
    private static PdfStandardFont MatchStandardFont(string name)
    {
        var bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        if (name.Contains("ZapfDingbats", StringComparison.OrdinalIgnoreCase) || name.Equals("ZaDb", StringComparison.Ordinal))
            return PdfStandardFont.ZapfDingbats;

        if (name.Equals("Symbol", StringComparison.Ordinal) || name.Equals("Symb", StringComparison.Ordinal))
            return PdfStandardFont.Symbol;

        if (name.Contains("Times", StringComparison.OrdinalIgnoreCase) || name.StartsWith("TiRo", StringComparison.Ordinal) || name.StartsWith("TiBo", StringComparison.Ordinal))
        {
            bold |= name.StartsWith("TiBo", StringComparison.Ordinal);
            return (bold, italic) switch
            {
                (true, true) => PdfStandardFont.TimesBoldItalic,
                (true, false) => PdfStandardFont.TimesBold,
                (false, true) => PdfStandardFont.TimesItalic,
                _ => PdfStandardFont.TimesRoman,
            };
        }

        if (name.Contains("Cour", StringComparison.OrdinalIgnoreCase))
        {
            return (bold, italic) switch
            {
                (true, true) => PdfStandardFont.CourierBoldOblique,
                (true, false) => PdfStandardFont.CourierBold,
                (false, true) => PdfStandardFont.CourierOblique,
                _ => PdfStandardFont.Courier,
            };
        }

        return (bold, italic) switch
        {
            (true, true) => PdfStandardFont.HelveticaBoldOblique,
            (true, false) => PdfStandardFont.HelveticaBold,
            (false, true) => PdfStandardFont.HelveticaOblique,
            _ => PdfStandardFont.Helvetica,
        };
    }

    private static PdfColor? ReadColor(PdfArray? components)
    {
        double Get(int index) => Math.Clamp(components.GetNumber(index), 0, 1);
        return components?.Count switch
        {
            1 => PdfColor.FromGray(Get(0)),
            3 => PdfColor.FromRgb(Get(0), Get(1), Get(2)),
            4 => PdfColor.FromCmyk(Get(0), Get(1), Get(2), Get(3)),
            _ => null,
        };
    }
}
