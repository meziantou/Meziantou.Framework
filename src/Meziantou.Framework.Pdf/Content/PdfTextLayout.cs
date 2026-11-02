using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Breaks a text into lines that fit a width: at the spaces, and inside a word only when the word alone is wider than
/// the width. The line feeds of the text end the paragraphs.
/// </summary>
internal static class PdfTextLayout
{
    /// <summary>A line: its text, without the spaces around it, the position of its first character in the text, and whether it ends a paragraph.</summary>
    internal readonly record struct Line(string Text, int Start, bool EndsParagraph);

    public static List<Line> Wrap(string text, PdfFont font, double fontSize, double width)
    {
        var lines = new List<Line>();
        var position = 0;
        foreach (var paragraph in text.Split('\n'))
        {
            var content = paragraph.TrimEnd('\r');
            WrapParagraph(content, position, font, fontSize, width, lines);
            position += paragraph.Length + 1;
        }

        return lines;
    }

    private static void WrapParagraph(string paragraph, int offset, PdfFont font, double fontSize, double width, List<Line> lines)
    {
        var current = new StringBuilder();
        var currentStart = -1;
        var index = 0;
        var first = lines.Count;
        while (index < paragraph.Length)
        {
            // The next word and the spaces before it
            var spaceStart = index;
            while (index < paragraph.Length && paragraph[index] == ' ')
            {
                index++;
            }

            var wordStart = index;
            while (index < paragraph.Length && paragraph[index] != ' ')
            {
                index++;
            }

            if (wordStart == index)
                break;

            var word = paragraph[wordStart..index];
            var candidate = current.Length == 0 ? word : current + paragraph[spaceStart..wordStart] + word;
            if (font.GetTextWidth(candidate, fontSize) <= width)
            {
                if (current.Length == 0)
                {
                    currentStart = wordStart;
                }

                current.Clear().Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(new Line(current.ToString(), offset + currentStart, EndsParagraph: false));
                current.Clear();
            }

            // A word wider than the box is broken between its characters
            var pieceStart = wordStart;
            var piece = new StringBuilder();
            var runeStart = 0;
            foreach (var rune in word.EnumerateRunes())
            {
                var length = rune.Utf16SequenceLength;
                var next = string.Concat(piece.ToString().AsSpan(), word.AsSpan(runeStart, length));
                if (piece.Length > 0 && font.GetTextWidth(next, fontSize) > width)
                {
                    lines.Add(new Line(piece.ToString(), offset + pieceStart, EndsParagraph: false));
                    pieceStart += piece.Length;
                    piece.Clear();
                }

                piece.Append(word, runeStart, length);
                runeStart += length;
            }

            current.Append(piece);
            currentStart = pieceStart;
        }

        if (current.Length > 0 || lines.Count == first)
        {
            lines.Add(new Line(current.ToString(), offset + Math.Max(currentStart, 0), EndsParagraph: true));
        }
        else
        {
            lines[^1] = lines[^1] with { EndsParagraph = true };
        }
    }
}
