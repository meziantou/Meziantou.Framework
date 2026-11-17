using System.Globalization;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>
    /// Stamps headers and footers on the pages, such as page numbers, a date or Bates numbers: the texts of the options,
    /// their tokens replaced for each page, at the top and the bottom of the page as it is displayed (its rotation
    /// applied). The texts are pagination artifacts (ISO 32000-2, 14.8.2.2), which assistive technologies skip.
    /// </summary>
    /// <param name="options">The texts and their style.</param>
    /// <exception cref="ArgumentException">A text has an unknown token or an unbalanced brace, uses <c>{date}</c> without <see cref="PdfHeaderFooterOptions.Date"/> or <c>{bates}</c> without <see cref="PdfHeaderFooterOptions.Bates"/>, or has a character the font cannot show.</exception>
    /// <seealso cref="PdfBates.Apply(IEnumerable{PdfDocument}, PdfHeaderFooterOptions)"/>
    public void AddHeaderFooter(PdfHeaderFooterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        (string? Template, int Row, int Column)[] slots =
        [
            (options.HeaderLeft, 0, 0), (options.HeaderCenter, 0, 1), (options.HeaderRight, 0, 2),
            (options.FooterLeft, 1, 0), (options.FooterCenter, 1, 1), (options.FooterRight, 1, 2),
        ];
        foreach (var (template, _, _) in slots)
        {
            if (template is not null)
            {
                ValidateTemplate(template, options);
            }
        }

        var font = options.Font ?? PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var pageCount = Pages.Count;
        var lastIndex = Math.Min(options.LastPageIndex ?? pageCount - 1, pageCount - 1);

        // The texts of every page first: a text the font cannot show leaves the document unchanged
        var texts = new List<(PdfPage Page, string Text, int Row, int Column)>();
        for (var index = options.FirstPageIndex; index <= lastIndex; index++)
        {
            var page = Pages[index];
            foreach (var (template, row, column) in slots)
            {
                if (template is null)
                    continue;

                var text = ExpandTemplate(template, options, page, index);
                if (text.Length == 0)
                    continue;

                if (!font.CanEncode(text))
                    throw new ArgumentException($"The font {font.PostScriptName} cannot show the text '{text}'", nameof(options));

                texts.Add((page, text, row, column));
            }
        }

        foreach (var group in texts.GroupBy(item => item.Page))
        {
            var page = group.Key;
            var box = page.VisibleBox;
            if (!(box.Width > 0 && box.Height > 0 && double.IsFinite(box.Width) && double.IsFinite(box.Height)))
                continue;

            var size = page.DisplaySize;
            var builder = page.CreateContentBuilder(options.Placement);
            foreach (var (_, text, row, column) in group)
            {
                var width = font.GetTextWidth(text, options.FontSize);
                var x = column switch
                {
                    0 => options.Margin,
                    1 => (size.Width - width) / 2,
                    _ => size.Width - options.Margin - width,
                };

                // The header below the top margin, the footer above the bottom margin
                var y = row == 0 ? size.Height - options.Margin - (Math.Max(font.Ascent, 0) / 1000 * options.FontSize) : options.Margin - (Math.Min(font.Descent, 0) / 1000 * options.FontSize);
                var properties = new PdfDictionary
                {
                    [PdfNames.Type] = PdfName.Create("Pagination"),
                    [PdfNames.Subtype] = PdfName.Create(row == 0 ? "Header" : "Footer"),
                };
                builder.BeginMarkedContent("Artifact", properties)
                    .SaveGraphicsState()
                    .Transform(page.DisplayToUserSpace)
                    .SetFillColor(options.Color)
                    .DrawText(text, font, options.FontSize, x, y)
                    .RestoreGraphicsState()
                    .EndMarkedContent();
            }

            builder.Complete();
        }
    }

    private static void ValidateTemplate(string template, PdfHeaderFooterOptions options)
    {
        foreach (var token in ParseTemplate(template))
        {
            switch (token.Token)
            {
                case null or "page" or "pages" or "label":
                    break;
                case "date" when options.Date is null:
                    throw new ArgumentException("A text uses {date}, but the options have no date", nameof(options));
                case "bates" when options.Bates is null:
                    throw new ArgumentException("A text uses {bates}, but the options have no Bates numbering", nameof(options));
                case "date" or "bates":
                    break;
                default:
                    throw new ArgumentException($"The token {{{token.Token}}} is unknown: use {{page}}, {{pages}}, {{label}}, {{date}} or {{bates}}", nameof(options));
            }
        }
    }

    private static string ExpandTemplate(string template, PdfHeaderFooterOptions options, PdfPage page, int index)
    {
        var builder = new StringBuilder(template.Length);
        foreach (var (literal, token) in ParseTemplate(template))
        {
            builder.Append(literal);
            switch (token)
            {
                case "page":
                    builder.Append((options.StartPageNumber + (long)index).ToString(CultureInfo.InvariantCulture));
                    break;
                case "pages":
                    builder.Append((options.PageCount ?? page.Document.Pages.Count).ToString(CultureInfo.InvariantCulture));
                    break;
                case "label":
                    builder.Append(page.Document.PageLabels.GetLabel(index) ?? (index + 1).ToString(CultureInfo.InvariantCulture));
                    break;
                case "date":
                    builder.Append(options.Date!.Value.ToString(options.DateFormat, CultureInfo.InvariantCulture));
                    break;
                case "bates":
                    builder.Append(options.Bates!.Format(options.Bates.StartNumber + index - options.FirstPageIndex));
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits a template into literals, each followed by a token or by none (at the end).</summary>
    private static List<(string Literal, string? Token)> ParseTemplate(string template)
    {
        var result = new List<(string, string?)>();
        var literal = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                literal.Append('{');
                i++;
            }
            else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i++;
            }
            else if (c == '{')
            {
                var end = template.IndexOf('}', i + 1, StringComparison.Ordinal);
                if (end < 0)
                    throw new ArgumentException($"The text '{template}' has a '{{' without '}}': write '{{{{' for a brace", nameof(template));

                result.Add((literal.ToString(), template[(i + 1)..end]));
                literal.Clear();
                i = end;
            }
            else if (c == '}')
            {
                throw new ArgumentException($"The text '{template}' has a '}}' without '{{': write '}}}}' for a brace", nameof(template));
            }
            else
            {
                literal.Append(c);
            }
        }

        result.Add((literal.ToString(), null));
        return result;
    }
}
