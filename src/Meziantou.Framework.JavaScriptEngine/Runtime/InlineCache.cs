namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The validity of the inline caches of the interpreter that depend on other objects than the receiver (a property found on
/// a prototype, or on an object that is not in a shared shape). The objects they depend on are flagged
/// (<see cref="ObjectFlags.InlineCacheDependency"/>): a change of their properties or of their prototype increments the
/// epoch, which invalidates all these caches at once. Such changes are rare once the prototypes are set up.
/// </summary>
internal static class InlineCache
{
    // Shared by all the engines (the bytecode, and so its caches, can be shared): a change in one engine only makes the
    // caches of the others miss once
    private static int s_epoch;

    /// <summary>Gets the current epoch: a cache that depends on other objects is valid while the epoch does not change.</summary>
    public static int Epoch => Volatile.Read(ref s_epoch);

    /// <summary>Invalidates the caches that depend on other objects than the receiver.</summary>
    public static void Invalidate() => Interlocked.Increment(ref s_epoch);
}
