namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Receives the contours of a flattened path, as straight segments.</summary>
internal abstract class PathSink
{
    /// <summary>
    /// Gets or sets the factor by which what the sink receives is enlarged before it is drawn: 1 unless a
    /// transformation follows. Curves are flattened that much finer, so that their chords stay within the tolerance on
    /// the canvas.
    /// </summary>
    public double Scale { get; set; } = 1;

    /// <summary>Starts a contour; the previous contour, if any, ends open.</summary>
    public abstract void MoveTo(double x, double y);

    /// <summary>Adds a segment from the current point.</summary>
    /// <param name="x">The horizontal coordinate of the end point.</param>
    /// <param name="y">The vertical coordinate of the end point.</param>
    /// <param name="smooth">
    /// <see langword="true"/> when the segment continues the curve of the previous segment: the vertex between them was
    /// created by flattening and is not a corner of the path.
    /// </param>
    public abstract void LineTo(double x, double y, bool smooth);

    /// <summary>Closes the contour with a segment to its first point.</summary>
    /// <param name="smooth"><see langword="true"/> when the contour is a closed curve: neither end of the closing segment is a corner of the path.</param>
    public abstract void Close(bool smooth);

    /// <summary>Ends the path; the current contour, if any, ends open.</summary>
    public abstract void End();
}
