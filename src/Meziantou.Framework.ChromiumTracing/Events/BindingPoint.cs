namespace Meziantou.Framework.ChromiumTracing;

/// <summary>
/// Specifies the binding point for Chromium tracing events.
/// </summary>
public enum BindingPoint
{
    /// <summary>
    /// Binds to the next slice in the timeline.
    /// </summary>
    NextSlice,

    /// <summary>
    /// Binds to the enclosing slice in the timeline.
    /// </summary>
    EnclosingSlice,
}
