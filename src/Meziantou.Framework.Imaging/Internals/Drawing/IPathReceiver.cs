namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Receives the elements of a shape made of straight segments and arcs: a path being built, or the sink of a draw call.</summary>
internal interface IPathReceiver
{
    /// <summary>Starts a contour.</summary>
    void MoveTo(double x, double y);

    /// <summary>Adds a straight segment from the current point.</summary>
    void LineTo(double x, double y);

    /// <summary>Adds an arc of at most a quarter turn from the current point.</summary>
    void Arc(scoped in ArcPiece piece);

    /// <summary>Closes the contour.</summary>
    void Close();
}
