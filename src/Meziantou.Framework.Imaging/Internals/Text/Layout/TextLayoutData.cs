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

    public required DecorationRectangle[] Decorations { get; init; }

    public required TextLine[] Lines { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public required bool IsTruncated { get; init; }

    public required bool HasMissingGlyphs { get; init; }
}
