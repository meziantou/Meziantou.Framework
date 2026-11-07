using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// The text of a paragraph made of the last lines of a paragraph processed before it: the end of the text of that
/// paragraph, and the offsets of its lines, starting at <see cref="FirstLineOffset"/>.
/// </summary>
internal sealed class ParagraphTextContinuation(StringSlice text, List<StringLineGroup.LineOffset> lineOffsets, int firstLineOffset)
{
    public StringSlice Text { get; } = text;

    public List<StringLineGroup.LineOffset> LineOffsets { get; } = lineOffsets;

    public int FirstLineOffset { get; } = firstLineOffset;
}
