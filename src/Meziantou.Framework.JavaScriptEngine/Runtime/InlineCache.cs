using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The inline caches of the interpreter: each property access of the bytecode remembers where it found the property
/// (<see cref="PropertyCacheEntry"/>), for the receivers that have the same <see cref="PropertyMap"/>.
/// </summary>
/// <remarks>
/// <para>
/// A site remembers up to <see cref="MaxPolymorphicEntries"/> entries (a chain linked by <see cref="PropertyCacheEntry.Next"/>,
/// the most recent first): a site that sees more kinds of receivers is megamorphic, and uses the cache of its engine
/// (<see cref="MegamorphicCache"/>), shared by all the megamorphic sites.
/// </para>
/// <para>
/// A cache that depends on other objects than the receiver (a property found on a prototype, or in the property table of
/// a receiver in dictionary mode) remembers the epoch: the objects it depends on are flagged
/// (<see cref="ObjectFlags.InlineCacheHolder"/>: the positions of its properties are remembered,
/// <see cref="ObjectFlags.InlineCachePrototype"/>: the absence of a property is remembered), and a change of their properties or of their prototype increments the
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
    /// <summary>The number of entries of a polymorphic site, above which it becomes megamorphic.</summary>
    public const int MaxPolymorphicEntries = 4;

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

    /// <summary>The site saw too many kinds of receivers, or missed too often: it uses the <see cref="MegamorphicCache"/> of the engine (Get and Set), or is not cached anymore.</summary>
    Megamorphic,

    /// <summary>A data property of the prototype chain of a primitive value (the prototype of its wrapper is <see cref="PropertyCacheEntry.Prototype"/>).</summary>
    PrimitivePrototype,

    /// <summary>A property of an object literal added by a transition to <see cref="PropertyCacheEntry.NextShape"/> (CreateDataProperty does not look at the prototypes).</summary>
    Define,
}

/// <summary>An entry of an inline cache (immutable once stored).</summary>
internal sealed class PropertyCacheEntry
{
    // The map of the megamorphic entries: never the map of an object
    private static readonly PropertyTable MegamorphicMap = new();

    /// <summary>Gets or sets the number of times the site missed before this entry (set before the entry is stored).</summary>
    public int Misses { get; set; }

    /// <summary>Gets or sets the next entry of a polymorphic site (set before the entry is stored).</summary>
    public PropertyCacheEntry? Next { get; set; }

    /// <summary>Gets or initializes the hash of the key of a megamorphic site (<see cref="MegamorphicCache"/>).</summary>
    public int KeyHash { get; init; }

    /// <summary>Gets or initializes the <see cref="Interpreter.GlobalEnvironment.LexicalVersion"/> of the realm (a property of the global object).</summary>
    public int LexicalVersion { get; init; }

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

    /// <summary>
    /// Gets or initializes the engine of a transition: the shapes with properties belong to an engine, and the static empty
    /// shapes are shared by all of them (an object of another engine must not get a shape of this one: the shapes are not
    /// thread-safe).
    /// </summary>
    public JsEngine? Engine { get; init; }

    /// <summary>Gets or initializes the binding of a global lexical declaration.</summary>
    public Interpreter.GlobalLexicalBinding? Binding { get; init; }

    /// <summary>Gets or initializes the realm of a global access.</summary>
    public JsRealm? Realm { get; init; }

    /// <summary>Creates the entry of a site that became megamorphic.</summary>
    public static PropertyCacheEntry CreateMegamorphic(in PropertyKey key) => new() { Kind = PropertyCacheKind.Megamorphic, Map = MegamorphicMap, KeyHash = key.GetHashCode() };

    /// <summary>Gets whether the entry depends on the epoch of <see cref="InlineCache"/> and the epoch changed (it never matches again).</summary>
    public bool IsStale => Kind is PropertyCacheKind.OwnTable or PropertyCacheKind.Prototype or PropertyCacheKind.Accessor or PropertyCacheKind.Add or PropertyCacheKind.PrimitivePrototype
        && Epoch != InlineCache.Epoch;

    /// <summary>Gets whether a new entry of the site applies to the receivers of this entry (it replaces this entry).</summary>
    public bool IsReplacedBy(PropertyCacheEntry entry) => ReferenceEquals(Map, entry.Map) && ReferenceEquals(Prototype, entry.Prototype) && (Map is not null || Kind == entry.Kind);

    /// <summary>Copies the entry without its next entry (the entries are immutable once stored: a new chain copies them).</summary>
    public PropertyCacheEntry CloneWithoutNext()
    {
        var clone = (PropertyCacheEntry)MemberwiseClone();
        clone.Next = null;
        return clone;
    }
}

/// <summary>
/// The inline cache shared by the megamorphic sites of an engine (a stub cache): the entries are found by the storage of
/// the receiver, its prototype and the key. A set has two ways: a new entry moves the previous one to the second way.
/// </summary>
/// <remarks>The engine runs on one thread at a time: the entries are not shared with the other engines.</remarks>
internal sealed class MegamorphicCache
{
    private const int GetBits = 10;
    private const int SetBits = 9;

    private readonly Slot[] _get = new Slot[1 << GetBits];
    private readonly Slot[] _set = new Slot[1 << SetBits];

    /// <summary>Finds the entry of a Get (to validate against the receiver: the map and the key match).</summary>
    public PropertyCacheEntry? FindGet(PropertyMap map, JsObject? prototype, in PropertyKey key, int keyHash) => Find(_get, GetBits, map, prototype, key, keyHash);

    /// <summary>Finds the entry of a Set (to validate against the receiver: the map and the key match).</summary>
    public PropertyCacheEntry? FindSet(PropertyMap map, JsObject? prototype, in PropertyKey key, int keyHash) => Find(_set, SetBits, map, prototype, key, keyHash);

    /// <summary>Stores the entry of a Get for the receivers of its map whose prototype is <paramref name="prototype"/>.</summary>
    public void StoreGet(PropertyCacheEntry entry, JsObject? prototype, in PropertyKey key, int keyHash) => Store(_get, GetBits, entry, prototype, key, keyHash);

    /// <summary>Stores the entry of a Set for the receivers of its map whose prototype is <paramref name="prototype"/>.</summary>
    public void StoreSet(PropertyCacheEntry entry, JsObject? prototype, in PropertyKey key, int keyHash) => Store(_set, SetBits, entry, prototype, key, keyHash);

    private static int GetIndex(int bits, PropertyMap map, JsObject? prototype, int keyHash)
    {
        var prototypeHash = prototype is null ? 0 : RuntimeHelpers.GetHashCode(prototype);
        var hash = (uint)(map.CacheHash ^ (prototypeHash * 31) ^ keyHash) * 0x9E3779B9u;
        return (int)(hash >> (32 - bits)) & ~1;
    }

    private static PropertyCacheEntry? Find(Slot[] slots, int bits, PropertyMap map, JsObject? prototype, in PropertyKey key, int keyHash)
    {
        var index = GetIndex(bits, map, prototype, keyHash);
        ref var first = ref slots[index];
        if (first.Entry is { } entry && ReferenceEquals(entry.Map, map) && first.Key.Equals(key))
            return entry;

        ref var second = ref slots[index + 1];
        if (second.Entry is { } other && ReferenceEquals(other.Map, map) && second.Key.Equals(key))
            return other;

        return null;
    }

    private static void Store(Slot[] slots, int bits, PropertyCacheEntry entry, JsObject? prototype, in PropertyKey key, int keyHash)
    {
        var index = GetIndex(bits, entry.Map!, prototype, keyHash);
        ref var first = ref slots[index];
        if (first.Entry is not { } previous || !ReferenceEquals(previous.Map, entry.Map) || !first.Key.Equals(key))
        {
            slots[index + 1] = first;
        }

        first = new Slot(entry, key);
    }

    private readonly record struct Slot(PropertyCacheEntry? Entry, PropertyKey Key);
}
