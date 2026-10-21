namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

internal enum FrameState : byte
{
    Running,

    /// <summary>The frame is suspended by a yield (generators).</summary>
    SuspendedYield,

    /// <summary>The frame is suspended by an await (async functions).</summary>
    SuspendedAwait,

    /// <summary>The frame returned (or threw).</summary>
    Completed,
}
