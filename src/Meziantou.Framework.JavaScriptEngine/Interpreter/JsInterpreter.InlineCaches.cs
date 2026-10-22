using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The property accesses with an inline cache (<see cref="PropertyCacheEntry"/>): the fast path, then the lookup that fills the cache.</summary>
internal static partial class JsInterpreter
{
    // A site that missed this many times (the objects it depends on keep changing) becomes megamorphic: each miss
    // allocated an entry
    private const int MaxCacheMisses = 16;

    /// <summary>
    /// Stores the entry of a cache after a miss: it is the first entry of the chain of the site, followed by the previous
    /// entries that are still valid (the site becomes megamorphic when it sees too many receivers, or misses too often).
    /// </summary>
    private static void StoreCacheEntry(Frame frame, int cache, PropertyCacheEntry entry, in PropertyKey key)
    {
        var caches = frame.Code.Caches;
        var previous = caches[cache];
        var misses = previous is not null ? previous.Misses + 1 : 0;
        if (misses > MaxCacheMisses)
        {
            caches[cache] = PropertyCacheEntry.CreateMegamorphic(key);
            return;
        }

        // The stored entries are immutable: the entries kept from the previous chain are copied
        var count = 1;
        var last = entry;
        for (var current = previous; current is not null; current = current.Next)
        {
            if (current.IsStale || current.IsReplacedBy(entry))
                continue;

            if (++count > InlineCache.MaxPolymorphicEntries)
            {
                caches[cache] = PropertyCacheEntry.CreateMegamorphic(key);
                return;
            }

            var copy = current.CloneWithoutNext();
            last.Next = copy;
            last = copy;
        }

        entry.Misses = misses;
        caches[cache] = entry;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Get
    // ------------------------------------------------------------------------------------------------------------------

    private static JsValue GetProperty(Frame frame, int name, int cache, JsValue target)
    {
        if (target.Cell is JsObject obj)
        {
            var entry = frame.Code.Caches[cache];
            if (entry is not null)
            {
                if (ReferenceEquals(obj.Map, entry.Map))
                {
                    if (entry.Kind == PropertyCacheKind.OwnSlot)
                        return obj.Slots![entry.Position];

                    if (entry.Epoch == InlineCache.Epoch)
                    {
                        switch (entry.Kind)
                        {
                            case PropertyCacheKind.OwnTable:
                                return obj.GetValueForCache(entry.Position, inTable: true);
                            case PropertyCacheKind.Prototype when ReferenceEquals(obj.PrototypeSlot, entry.Prototype):
                                return entry.Holder!.GetValueForCache(entry.Position, entry.InTable);
                            case PropertyCacheKind.Accessor when entry.Prototype is null || ReferenceEquals(obj.PrototypeSlot, entry.Prototype):
                                return CallGetter((entry.Holder ?? obj).GetValueForCache(entry.Position, entry.InTable), target);
                        }
                    }
                }
                else if (entry.Kind == PropertyCacheKind.ArrayLength && obj is JsArray array)
                {
                    return array.Length;
                }
            }

            return GetPropertyMiss(frame, name, cache, obj, target);
        }

        // A primitive value: the length of a string, or a property of the prototype of the primitive (a method)
        if (frame.Code.Caches[cache] is { } primitiveEntry && TryGetPrimitiveFromCache(frame.Realm, primitiveEntry, target, out var primitiveValue))
            return primitiveValue;

        return GetPrimitivePropertyMiss(frame, name, cache, target);
    }

    /// <summary>Reads a property of an object with an entry of a cache: false when the entry does not apply to the object.</summary>
    private static bool TryGetFromCache(PropertyCacheEntry entry, JsObject obj, JsValue target, out JsValue value)
    {
        if (ReferenceEquals(obj.Map, entry.Map))
        {
            switch (entry.Kind)
            {
                case PropertyCacheKind.OwnSlot:
                    value = obj.Slots![entry.Position];
                    return true;
                case PropertyCacheKind.OwnTable when entry.Epoch == InlineCache.Epoch:
                    value = obj.GetValueForCache(entry.Position, inTable: true);
                    return true;
                case PropertyCacheKind.Prototype when entry.Epoch == InlineCache.Epoch && ReferenceEquals(obj.PrototypeSlot, entry.Prototype):
                    value = entry.Holder!.GetValueForCache(entry.Position, entry.InTable);
                    return true;
                case PropertyCacheKind.Accessor when entry.Epoch == InlineCache.Epoch && (entry.Prototype is null || ReferenceEquals(obj.PrototypeSlot, entry.Prototype)):
                    value = CallGetter((entry.Holder ?? obj).GetValueForCache(entry.Position, entry.InTable), target);
                    return true;
            }
        }
        else if (entry.Kind == PropertyCacheKind.ArrayLength && obj is JsArray array)
        {
            value = array.Length;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads a property of a primitive value with an entry of a cache: false when the entry does not apply to the value.</summary>
    private static bool TryGetPrimitiveFromCache(JsRealm realm, PropertyCacheEntry entry, JsValue target, out JsValue value)
    {
        if (entry.Kind == PropertyCacheKind.StringLength)
        {
            if (target.Cell is JsString str)
            {
                value = str.Length;
                return true;
            }
        }
        else if (entry.Kind == PropertyCacheKind.PrimitivePrototype && entry.Epoch == InlineCache.Epoch
            && ReferenceEquals(GetPrimitivePrototypeOrNull(realm, target), entry.Prototype))
        {
            value = entry.Holder!.GetValueForCache(entry.Position, entry.InTable);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the prototype of a primitive value (the object of its wrapper), or null for undefined and null.</summary>
    private static JsObject? GetPrimitivePrototypeOrNull(JsRealm realm, JsValue value)
    {
        return value.Cell switch
        {
            JsString => realm.Intrinsics.StringPrototype,
            NumberCell => realm.Intrinsics.NumberPrototype,
            BooleanCell => realm.Intrinsics.BooleanPrototype,
            JsSymbol => realm.Intrinsics.SymbolPrototype,
            JsBigInt => realm.Intrinsics.BigIntPrototype,
            _ => null,
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue GetPrimitivePropertyMiss(Frame frame, int name, int cache, JsValue target)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        var head = frame.Code.Caches[cache];
        if (head is { Kind: PropertyCacheKind.Megamorphic })
        {
            // Not cached
        }
        else
        {
            // The other entries of a polymorphic site (the first one was checked)
            for (var entry = head?.Next; entry is not null; entry = entry.Next)
            {
                if (TryGetPrimitiveFromCache(frame.Realm, entry, target, out var value))
                    return value;
            }

            if (target.Cell is JsString && key.Equals(LengthKey))
            {
                StoreCacheEntry(frame, cache, new PropertyCacheEntry { Kind = PropertyCacheKind.StringLength }, key);
            }
            else if (GetPrimitivePrototypeOrNull(frame.Realm, target) is { } prototype && !key.IsArrayIndex)
            {
                // A data property of the prototype chain of the primitive (an accessor needs the primitive as this: not cached)
                for (var holder = prototype; holder is not null; holder = holder.PrototypeSlot)
                {
                    if (holder.GetMapForCache(key) is not { } holderMap)
                        break;

                    if (holder.TryFindOwnForCache(key, out var position, out var flags))
                    {
                        if ((flags & PropertyFlags.Accessor) == 0)
                        {
                            for (var dependency = prototype; dependency is not null; dependency = dependency.PrototypeSlot)
                            {
                                dependency.MarkInlineCachePrototype();
                                if (ReferenceEquals(dependency, holder))
                                    break;
                            }

                            StoreCacheEntry(frame, cache, new PropertyCacheEntry
                            {
                                Kind = PropertyCacheKind.PrimitivePrototype,
                                Prototype = prototype,
                                Holder = holder,
                                Position = position,
                                InTable = holderMap is PropertyTable,
                                Epoch = InlineCache.Epoch,
                            }, key);
                        }

                        break;
                    }
                }
            }
        }

        return GetPropertyOfPrimitive(frame.Realm, target, key);
    }

    private static JsValue CallGetter(JsValue accessor, JsValue receiver)
    {
        var getter = ((AccessorPair)accessor.Cell!).Getter;
        return getter.IsUndefined ? JsValue.Undefined : getter.AsObject().Call(receiver, []);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue GetPropertyMiss(Frame frame, int name, int cache, JsObject obj, JsValue target)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        var head = frame.Code.Caches[cache];
        if (head is { Kind: PropertyCacheKind.Megamorphic })
            return GetPropertyMegamorphic(frame.Realm.Engine, head, obj, key, target);

        // The other entries of a polymorphic site (the first one was checked)
        for (var current = head?.Next; current is not null; current = current.Next)
        {
            if (TryGetFromCache(current, obj, target, out var value))
                return value;
        }

        if (CreateGetEntry(obj, key) is { } entry)
        {
            StoreCacheEntry(frame, cache, entry, key);
        }

        return obj.Get(key, target);
    }

    /// <summary>A Get of a megamorphic site: the entries are in the cache of the engine.</summary>
    private static JsValue GetPropertyMegamorphic(JsEngine engine, PropertyCacheEntry site, JsObject obj, in PropertyKey key, JsValue target)
    {
        var megamorphicCache = engine.MegamorphicCache;
        if (obj.Map is { } map && megamorphicCache.FindGet(map, obj.PrototypeSlot, key, site.KeyHash) is { } cached && TryGetFromCache(cached, obj, target, out var value))
            return value;

        if (obj is JsArray array && key.Equals(LengthKey))
            return array.Length;

        // The entry is valid for the object: it gives the value without a second lookup
        if (CreateGetEntry(obj, key) is { Map: not null } entry)
        {
            megamorphicCache.StoreGet(entry, obj.PrototypeSlot, key, site.KeyHash);
            if (TryGetFromCache(entry, obj, target, out value))
                return value;
        }

        return obj.Get(key, target);
    }

    /// <summary>Finds a property for a Get with an inline cache; null when the access cannot be cached.</summary>
    private static PropertyCacheEntry? CreateGetEntry(JsObject obj, in PropertyKey key)
    {
        if (obj is JsArray && key.Equals(LengthKey))
            return new PropertyCacheEntry { Kind = PropertyCacheKind.ArrayLength };

        var map = obj.GetMapForCache(key);
        if (map is null)
            return null;

        int position;
        PropertyFlags flags;
        if (obj.TryFindOwnForCache(key, out position, out flags))
        {
            var ownInTable = map is PropertyTable;
            if (ownInTable)
            {
                obj.MarkInlineCacheHolder();
            }

            if ((flags & PropertyFlags.Accessor) != 0)
                return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.Accessor, Position = position, InTable = ownInTable, Epoch = InlineCache.Epoch };

            if (!ownInTable)
                return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.OwnSlot, Position = position };

            return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.OwnTable, Position = position, Epoch = InlineCache.Epoch };
        }

        var prototype = obj.PrototypeSlot;
        for (var holder = prototype; holder is not null; holder = holder.PrototypeSlot)
        {
            var holderMap = holder.GetMapForCache(key);
            if (holderMap is null)
                return null;

            if (holder.TryFindOwnForCache(key, out position, out flags))
            {
                MarkPrototypes(obj, map, holder);
                return new PropertyCacheEntry
                {
                    Map = map,
                    Kind = (flags & PropertyFlags.Accessor) != 0 ? PropertyCacheKind.Accessor : PropertyCacheKind.Prototype,
                    Prototype = prototype,
                    Holder = holder,
                    Position = position,
                    InTable = holderMap is PropertyTable,
                    Epoch = InlineCache.Epoch,
                };
            }
        }

        return null;
    }

    /// <summary>Marks the objects an entry depends on: the prototypes of the receiver up to the holder (included), and the receiver when it is in dictionary mode.</summary>
    private static void MarkPrototypes(JsObject receiver, PropertyMap receiverMap, JsObject? last)
    {
        if (receiverMap is PropertyTable)
        {
            receiver.MarkInlineCachePrototype();
        }

        for (var prototype = receiver.PrototypeSlot; prototype is not null; prototype = prototype.PrototypeSlot)
        {
            prototype.MarkInlineCachePrototype();
            if (ReferenceEquals(prototype, last))
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Set
    // ------------------------------------------------------------------------------------------------------------------

    private static void SetProperty(Frame frame, int name, int cache, JsValue target, JsValue value)
    {
        if (target.Cell is JsObject obj && frame.Code.Caches[cache] is { } entry && ReferenceEquals(obj.Map, entry.Map))
        {
            switch (entry.Kind)
            {
                case PropertyCacheKind.OwnSlot:
                    obj.Slots![entry.Position] = value;
                    return;
                case PropertyCacheKind.Add when ReferenceEquals(obj.PrototypeSlot, entry.Prototype) && entry.Epoch == InlineCache.Epoch && obj.IsExtensibleSlot && ReferenceEquals(entry.Engine, frame.Realm.Engine):
                    obj.AddPropertyForCache(entry.NextShape!, value);
                    return;
                case PropertyCacheKind.OwnTable when entry.Epoch == InlineCache.Epoch:
                    obj.SetValueForCache(entry.Position, inTable: true, value);
                    return;
            }
        }

        SetPropertyMiss(frame, name, cache, target, value);
    }

    /// <summary>Writes a property of an object with an entry of a cache: false when the entry does not apply to the object.</summary>
    private static bool TrySetFromCache(PropertyCacheEntry entry, JsObject obj, JsValue value, JsEngine engine)
    {
        if (!ReferenceEquals(obj.Map, entry.Map))
            return false;

        switch (entry.Kind)
        {
            case PropertyCacheKind.OwnSlot:
                obj.Slots![entry.Position] = value;
                return true;
            case PropertyCacheKind.Add when ReferenceEquals(obj.PrototypeSlot, entry.Prototype) && entry.Epoch == InlineCache.Epoch && obj.IsExtensibleSlot && ReferenceEquals(entry.Engine, engine):
                obj.AddPropertyForCache(entry.NextShape!, value);
                return true;
            case PropertyCacheKind.OwnTable when entry.Epoch == InlineCache.Epoch:
                obj.SetValueForCache(entry.Position, inTable: true, value);
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetPropertyMiss(Frame frame, int name, int cache, JsValue target, JsValue value)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        if (target.Cell is JsObject obj)
        {
            var engine = frame.Realm.Engine;
            var head = frame.Code.Caches[cache];
            if (head is { Kind: PropertyCacheKind.Megamorphic })
            {
                var megamorphicCache = engine.MegamorphicCache;
                if (obj.Map is { } map && megamorphicCache.FindSet(map, obj.PrototypeSlot, key, head.KeyHash) is { } cached && TrySetFromCache(cached, obj, value, engine))
                    return;

                if (CreateSetEntry(obj, key) is { Map: not null } megamorphicEntry)
                {
                    megamorphicCache.StoreSet(megamorphicEntry, obj.PrototypeSlot, key, head.KeyHash);
                }
            }
            else
            {
                // The other entries of a polymorphic site (the first one was checked)
                for (var current = head?.Next; current is not null; current = current.Next)
                {
                    if (TrySetFromCache(current, obj, value, engine))
                        return;
                }

                if (CreateSetEntry(obj, key) is { } entry)
                {
                    StoreCacheEntry(frame, cache, entry, key);
                }
            }
        }

        SetPropertyOfValue(target, key, value, frame.Code.IsStrict);
    }

    /// <summary>Finds where a Set stores a property, for an inline cache (before the Set); null when the access cannot be cached.</summary>
    private static PropertyCacheEntry? CreateSetEntry(JsObject obj, in PropertyKey key)
    {
        var map = obj.GetMapForCache(key);
        if (map is null)
            return null;

        if (obj.TryFindOwnForCache(key, out var position, out var flags))
        {
            if ((flags & (PropertyFlags.Accessor | PropertyFlags.Writable)) != PropertyFlags.Writable)
                return null;

            if (map is Shape)
                return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.OwnSlot, Position = position };

            obj.MarkInlineCacheHolder();
            return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.OwnTable, Position = position, Epoch = InlineCache.Epoch };
        }

        // A new property: no setter nor read-only property in the prototypes, then a transition of the shape
        if (map is not Shape shape || !obj.IsExtensibleSlot)
            return null;

        JsObject? last = null;
        for (var holder = obj.PrototypeSlot; holder is not null; holder = holder.PrototypeSlot)
        {
            if (holder.GetMapForCache(key) is null)
                return null;

            last = holder;
            if (holder.TryFindOwnForCache(key, out _, out flags))
            {
                if ((flags & (PropertyFlags.Accessor | PropertyFlags.Writable)) != PropertyFlags.Writable)
                    return null;

                break;
            }
        }

        if (shape.AddProperty(key, PropertyFlags.Default) is not { } next)
            return null;

        MarkPrototypes(obj, map, last);
        return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.Add, Prototype = obj.PrototypeSlot, NextShape = next, Epoch = InlineCache.Epoch, Engine = JsEngine.Current };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Object literals
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>CreateDataPropertyOrThrow of a property of an object literal.</summary>
    private static void DefineField(Frame frame, int name, int cache, JsValue target, JsValue value)
    {
        var obj = (JsObject)target.Cell!;
        if (frame.Code.Caches[cache] is { } entry && ReferenceEquals(obj.Map, entry.Map) && obj.IsExtensibleSlot && ReferenceEquals(entry.Engine, frame.Realm.Engine))
        {
            obj.AddPropertyForCache(entry.NextShape!, value);
            return;
        }

        DefineFieldMiss(frame, name, cache, obj, value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineFieldMiss(Frame frame, int name, int cache, JsObject obj, JsValue value)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        var head = frame.Code.Caches[cache];
        if (!obj.IsExotic && head is not { Kind: PropertyCacheKind.Megamorphic })
        {
            // The other entries of a polymorphic site (the first one was checked)
            var engine = frame.Realm.Engine;
            for (var current = head?.Next; current is not null; current = current.Next)
            {
                if (ReferenceEquals(obj.Map, current.Map) && obj.IsExtensibleSlot && ReferenceEquals(current.Engine, engine))
                {
                    obj.AddPropertyForCache(current.NextShape!, value);
                    return;
                }
            }

            if (obj.GetMapForCache(key) is Shape shape && shape.Find(key) < 0 && shape.AddProperty(key, PropertyFlags.Default) is { } next)
            {
                // CreateDataProperty does not look at the prototypes: the shape is enough
                StoreCacheEntry(frame, cache, new PropertyCacheEntry { Map = shape, Kind = PropertyCacheKind.Define, NextShape = next, Engine = engine }, key);
            }
        }

        obj.CreateDataPropertyOrThrow(key, value);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Globals
    // ------------------------------------------------------------------------------------------------------------------

    private static JsValue LoadGlobal(Frame frame, int name, int cache)
    {
        var entry = frame.Code.Caches[cache];
        if (entry is not null && ReferenceEquals(entry.Realm, frame.Realm))
        {
            if (entry.Kind == PropertyCacheKind.GlobalLexical)
            {
                var value = entry.Binding!.Value;
                if (!value.IsHole)
                    return value;
            }
            else if (entry.Epoch == InlineCache.Epoch && entry.LexicalVersion == frame.Realm.GlobalEnvironment.LexicalVersion)
            {
                var global = entry.Holder!;
                if (ReferenceEquals(global.Map, entry.Map))
                    return global.GetValueForCache(entry.Position, entry.InTable);
            }
        }

        return LoadGlobalMiss(frame, name, cache, throwIfMissing: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue LoadGlobalMiss(Frame frame, int name, int cache, bool throwIfMissing)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        CacheGlobalBinding(frame, key, cache);
        return LoadGlobal(frame.Realm, key, throwIfMissing);
    }

    /// <summary>
    /// ResolveBinding of an identifier that is not declared in the code, in strict code: the global environment when it has
    /// the binding, undefined when the reference is unresolvable. The cache of the instruction remembers a global binding that
    /// exists (as for <see cref="LoadGlobal(Frame, int, int)"/>): an unresolvable reference is the slow path.
    /// </summary>
    private static JsValue ResolveGlobal(Frame frame, int name, int cache)
    {
        var realm = frame.Realm;
        var entry = frame.Code.Caches[cache];
        if (entry is not null && ReferenceEquals(entry.Realm, realm))
        {
            // A global lexical binding is never removed (it exists even before its initialization)
            if (entry.Kind == PropertyCacheKind.GlobalLexical)
                return new JsValue(realm.GlobalEnvironment);

            if (entry.Epoch == InlineCache.Epoch && entry.LexicalVersion == realm.GlobalEnvironment.LexicalVersion && ReferenceEquals(entry.Holder!.Map, entry.Map))
                return new JsValue(realm.GlobalEnvironment);
        }

        return ResolveGlobalMiss(frame, name, cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsValue ResolveGlobalMiss(Frame frame, int name, int cache)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        CacheGlobalBinding(frame, key, cache);
        return ResolveGlobalBinding(frame.Realm, key);
    }

    /// <summary>Remembers the global binding of a name in the cache of an instruction (a global lexical binding, or a data property of the global object).</summary>
    private static void CacheGlobalBinding(Frame frame, in PropertyKey key, int cache)
    {
        var realm = frame.Realm;
        if (key.IsString && realm.GlobalEnvironment.LexicalBindings.TryGetValue(key.AsString(), out var binding))
        {
            // The bindings of the global lexical declarations are never removed
            frame.Code.Caches[cache] = new PropertyCacheEntry { Kind = PropertyCacheKind.GlobalLexical, Binding = binding, Realm = realm };
        }
        else
        {
            // A data property of the global object (valid until a script declares global lexical bindings in the realm: they shadow the global object)
            var global = realm.GlobalObject;
            if (global.GetMapForCache(key) is { } map && global.TryFindOwnForCache(key, out var position, out var flags) && (flags & PropertyFlags.Accessor) == 0)
            {
                global.MarkInlineCacheHolder();
                frame.Code.Caches[cache] = new PropertyCacheEntry
                {
                    Kind = PropertyCacheKind.OwnTable,
                    Map = map,
                    Holder = global,
                    Position = position,
                    InTable = map is PropertyTable,
                    Epoch = InlineCache.Epoch,
                    LexicalVersion = realm.GlobalEnvironment.LexicalVersion,
                    Realm = realm,
                };
            }
        }
    }
}
