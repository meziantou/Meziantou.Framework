namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The inline caches of the interpreter: each property access of the bytecode remembers where it found the property the
/// last time (<see cref="PropertyCacheEntry"/>), for the receivers that have the same <see cref="PropertyMap"/>.
/// </summary>
/// <remarks>
/// <para>
/// A cache that depends on other objects than the receiver (a property found on a prototype, or in the property table of
/// a receiver in dictionary mode) remembers the epoch: the objects it depends on are flagged
/// (<see cref="ObjectFlags.InlineCacheDependency"/>), and a change of their properties or of their prototype increments the
/// epoch, which invalidates all these caches at once. Such changes are rare once the prototypes are set up.
/// </para>
/// <para>
/// The caches are in the <see cref="Compiler.FunctionTemplate"/>, which the engines of several threads can share: an entry
/// is immutable and replaced as a whole, and it only matches the objects of the engine that created it (the shapes with
/// properties belong to an engine, the static empty shapes are checked with the prototype).
/// </para>
/// </remarks>
internal static class InlineCache
{
    private static int s_epoch;

    /// <summary>Gets the current epoch: a cache that depends on other objects is valid while the epoch does not change.</summary>
    public static int Epoch => Volatile.Read(ref s_epoch);

    /// <summary>Invalidates the caches that depend on other objects than the receiver.</summary>
    public static void Invalidate() => Interlocked.Increment(ref s_epoch);
}

/// <summary>What a <see cref="PropertyCacheEntry"/> remembers.</summary>
internal enum PropertyCacheKind : byte
{
    /// <summary>A data property in the slots of the receiver (Get, and Set when it is writable).</summary>
    OwnSlot,

    /// <summary>A data property in the property table of the receiver (valid while the epoch does not change).</summary>
    OwnTable,

    /// <summary>A data property of a prototype (the holder); valid while the prototype of the receiver and the epoch do not change.</summary>
    Prototype,

    /// <summary>An accessor property of the receiver or of a prototype: the getter or the setter is called.</summary>
    Accessor,

    /// <summary>A new property added by a transition to <see cref="PropertyCacheEntry.NextShape"/> (Set: no setter nor read-only property in the prototypes).</summary>
    Add,

    /// <summary>The length of an array.</summary>
    ArrayLength,

    /// <summary>A global lexical binding (let, const, class of a script).</summary>
    GlobalLexical,

    /// <summary>The length of a string.</summary>
    StringLength,

    /// <summary>A data property of the prototype chain of a primitive value (the prototype of its wrapper is <see cref="PropertyCacheEntry.Prototype"/>).</summary>
    PrimitivePrototype,
}

/// <summary>An entry of an inline cache (immutable).</summary>
internal sealed class PropertyCacheEntry
{
    /// <summary>Gets or initializes the storage of the receivers the entry applies to.</summary>
    public PropertyMap? Map { get; init; }

    public PropertyCacheKind Kind { get; init; }

    /// <summary>Gets or initializes the prototype of the receiver (the entries that depend on the prototypes).</summary>
    public JsObject? Prototype { get; init; }

    /// <summary>Gets or initializes the object that has the property (a prototype, or the receiver for an entry of an accessor).</summary>
    public JsObject? Holder { get; init; }

    /// <summary>Gets or initializes the position of the property in the slots or in the property table of the holder.</summary>
    public int Position { get; init; }

    /// <summary>Gets or initializes whether the position is in the property table of the holder.</summary>
    public bool InTable { get; init; }

    /// <summary>Gets or initializes the epoch of <see cref="InlineCache"/> when the entry was created (the entries that depend on other objects).</summary>
    public int Epoch { get; init; }

    /// <summary>Gets or initializes the shape after the transition (<see cref="PropertyCacheKind.Add"/>).</summary>
    public Shape? NextShape { get; init; }

    /// <summary>Gets or initializes the binding of a global lexical declaration.</summary>
    public Interpreter.GlobalLexicalBinding? Binding { get; init; }

    /// <summary>Gets or initializes the realm of a global access.</summary>
    public JsRealm? Realm { get; init; }
}
