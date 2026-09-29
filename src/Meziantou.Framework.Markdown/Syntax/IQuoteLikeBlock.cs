using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// A container whose lines start with a marker, like a <see cref="QuoteBlock"/>. With trivia, it records the marker of each of its
/// lines, and the lazy continuation lines that have no marker, so that the roundtrip renderer writes the markers back.
/// </summary>
internal interface IQuoteLikeBlock
{
    /// <summary>
    /// Gets the trivia of each line of the block.
    /// </summary>
    List<QuoteBlockLine> QuoteLines { get; }

    /// <summary>
    /// Gets the marker that starts the lines of the block, such as <c>&gt;</c>.
    /// </summary>
    string Marker { get; }

    /// <summary>
    /// Gets or sets the empty lines after the block.
    /// </summary>
    List<StringSlice>? LinesAfter { get; set; }
}
