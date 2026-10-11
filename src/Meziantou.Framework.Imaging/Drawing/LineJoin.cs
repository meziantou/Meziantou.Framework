namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The shape of the corner between two segments of a stroke.</summary>
public enum LineJoin
{
    /// <summary>
    /// The outer edges are extended until they meet. When the corner is sharper than <see cref="Pen.MiterLimit"/> allows,
    /// the join is a <see cref="Bevel"/>. This is the default.
    /// </summary>
    Miter = 0,

    /// <summary>The corner is rounded with an arc centered on the vertex.</summary>
    Round = 1,

    /// <summary>The corner is cut by a straight line between the outer edges.</summary>
    Bevel = 2,
}
