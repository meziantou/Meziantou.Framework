namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The shape of the ends of an open stroke.</summary>
public enum LineCap
{
    /// <summary>The stroke stops at the end point. This is the default.</summary>
    Butt = 0,

    /// <summary>The stroke ends with a half disk centered on the end point.</summary>
    Round = 1,

    /// <summary>The stroke is extended by half its width past the end point.</summary>
    Square = 2,
}
