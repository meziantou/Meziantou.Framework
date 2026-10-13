using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What <see cref="TextLayoutEngine"/> produces: the content of a <see cref="TextLayout"/>.</summary>
internal sealed class TextLayoutData
{
    public required Font[] Fonts { get; init; }

    /// <summary>Gets the paints of the layout; <see langword="null"/> is the brush of the draw call.</summary>
    public required Brush?[] Brushes { get; init; }

    public required GlyphRun[] Runs { get; init; }

    public required int[] Glyphs { get; init; }

    /// <summary>Gets the origin of each glyph, on its baseline: x then y, from the top-left corner of the layout.</summary>
    public required double[] Positions { get; init; }

    /// <summary>
    /// Gets, for each glyph, the horizontal middle of what it is placed with on a path: its cluster (a character and its
    /// marks, a ligature), from the left of the layout.
    /// </summary>
    public required double[] Anchors { get; init; }

    public required DecorationRectangle[] Decorations { get; init; }

    public required TextLine[] Lines { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public required bool IsTruncated { get; init; }

    public required bool HasMissingGlyphs { get; init; }

    /// <summary>Gets the number of UTF-16 code units of the text.</summary>
    public required int TextLength { get; init; }

    /// <summary>Gets the clusters of every line, from the left of each line: what the positions of the text are found with.</summary>
    public required TextHitBox[] HitBoxes { get; init; }

    /// <summary>Gets the index of the first cluster of each line in <see cref="HitBoxes"/>, and after them the number of clusters.</summary>
    public required int[] HitBoxStarts { get; init; }

    /// <summary>Gets the ends of the characters of the clusters that hold several, from the start of their cluster.</summary>
    public required int[] CharacterBoundaries { get; init; }
}
