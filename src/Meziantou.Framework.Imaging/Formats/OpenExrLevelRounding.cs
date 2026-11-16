namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How the size of a level of a tiled OpenEXR file is rounded when it is halved (<see cref="OpenExrTiling.LevelRounding"/>).</summary>
public enum OpenExrLevelRounding
{
    /// <summary>Halved sizes are rounded down (<c>ROUND_DOWN</c>): a level of 5 pixels is followed by 2, then 1. This is the default.</summary>
    Down = 0,

    /// <summary>Halved sizes are rounded up (<c>ROUND_UP</c>): a level of 5 pixels is followed by 3, 2, then 1.</summary>
    Up = 1,
}
