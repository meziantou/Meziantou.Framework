namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Lines of a paragraph that a paragraph extension split: a block of the extension, whose parts are on its lines, or the
/// lines around it, which stay a paragraph. The lines are indexes of the lines of the block parser.
/// </summary>
internal sealed record ParagraphSegment(int ExtensionIndex, int LineStart, int LineCount, ParagraphExtensionRange[]? Ranges)
{
    public static ParagraphSegment Paragraph(int lineStart, int lineCount) => new(-1, lineStart, lineCount, Ranges: null);
}
