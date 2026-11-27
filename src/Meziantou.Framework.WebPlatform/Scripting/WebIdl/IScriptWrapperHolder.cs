using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A platform object that stores its script object (see <c>WebIdlRealm.GetWrapper</c>) rather than the table of the agent,
/// whose entries each take a GC handle: the handles are allocated under a lock that the threads share, and every collection
/// scans them. The object stores the script object of the realm of <see cref="ScriptWrapperWindow"/> only: the object
/// references this window, which references its script engine, so the script object does not keep the agent alive longer.
/// </summary>
internal interface IScriptWrapperHolder
{
    /// <summary>The script object of the object and the agent that created it, or null.</summary>
    ScriptWrapperSlot? ScriptWrapper { get; set; }

    /// <summary>The window that the object keeps alive (its realm), or null.</summary>
    Window? ScriptWrapperWindow { get; }
}
