using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A vertical layout as it is laid out, before it is turned: each of its lines as a horizontal line read from its
/// top, and its lines stacked from the first one.
/// </summary>
/// <remarks>
/// <para>
/// The engine lays out a vertical text in the coordinates of its lines: <c>u</c> along a line, from its top, and
/// <c>v</c> across the lines, from the side the ascent of the text that is on its side points to (the right of the
/// line). These are the coordinates of a horizontal layout. The point <c>(u, v)</c> of a line is drawn at
/// <c>(right - (v - top), u)</c>, where <c>top</c> is where the line starts across the lines and <c>right</c> is the
/// right edge the line has on the image (<see cref="LineRights"/>).
/// </para>
/// <para>
/// What is drawn at a point is stored turned, with the layout. What works along a line keeps these coordinates: the
/// positions of the text (carets, selections, hit tests), and a text that follows a path.
/// </para>
/// </remarks>
internal sealed class VerticalLayoutData
{
    /// <summary>Gets the lines, as those of a horizontal layout.</summary>
    public required TextLine[] Lines { get; init; }

    /// <summary>Gets the right edge of each line on the image, from the left of the layout.</summary>
    public required double[] LineRights { get; init; }

    /// <summary>Gets the origin of each glyph in the coordinates of the lines: along then across.</summary>
    public required double[] Positions { get; init; }

    /// <summary>Gets the decorations in the coordinates of the lines.</summary>
    public required DecorationRectangle[] Decorations { get; init; }

    /// <summary>Gets the backgrounds in the coordinates of the lines.</summary>
    public required DecorationRectangle[] Backgrounds { get; init; }

    /// <summary>Gets a value indicating whether the lines follow each other from the left to the right.</summary>
    public required bool IsLeftToRight { get; init; }

    /// <summary>Gets the length of the longest line.</summary>
    public required double InlineSize { get; init; }
}
