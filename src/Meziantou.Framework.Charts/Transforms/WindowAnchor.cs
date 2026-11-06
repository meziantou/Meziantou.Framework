namespace Meziantou.Framework.Charts;

/// <summary>Where the window of a <see cref="WindowTransform"/> is, relative to its row.</summary>
public enum WindowAnchor
{
    /// <summary>The window is centered on the row: a centered moving average.</summary>
    Middle,

    /// <summary>The window ends at the row: a trailing moving average, which only uses the past.</summary>
    End,

    /// <summary>The window starts at the row.</summary>
    Start,
}
