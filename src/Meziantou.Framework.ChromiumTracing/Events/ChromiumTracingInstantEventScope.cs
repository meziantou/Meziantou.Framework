namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Specifies the scope of an instant event.</summary>
public enum ChromiumTracingInstantEventScope
{
    /// <summary>The event is global across all processes and threads.</summary>
    Global,

    /// <summary>The event is scoped to a specific process.</summary>
    Process,

    /// <summary>The event is scoped to a specific thread.</summary>
    Thread,
}
