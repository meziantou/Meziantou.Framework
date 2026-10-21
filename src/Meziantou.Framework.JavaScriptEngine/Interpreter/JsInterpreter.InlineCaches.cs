using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The property accesses with an inline cache (<see cref="PropertyCacheEntry"/>): the fast path, then the lookup that fills the cache.</summary>
internal static partial class JsInterpreter
{
    // A site that missed this many times (it sees many shapes, or the objects it depends on keep changing) is not cached
    // anymore: each miss allocated an entry
    private const int MaxCacheMisses = 16;

    private static bool IsMegamorphic(Frame frame, int cache) => frame.Code.Caches[cache] is { Kind: PropertyCacheKind.Megamorphic };

    /// <summary>Stores the entry of a cache after a miss (the site becomes megamorphic when it misses too often).</summary>
    private static void StoreCacheEntry(Frame frame, int cache, PropertyCacheEntry entry)
    {
        var caches = frame.Code.Caches;
        var misses = caches[cache] is { } previous ? previous.Misses + 1 : 0;
        if (misses > MaxCacheMisses)
        {
            caches[cache] = PropertyCacheEntry.Megamorphic;
            return;
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
        if (frame.Code.Caches[cache] is { } primitiveEntry)
        {
            if (primitiveEntry.Kind == PropertyCacheKind.StringLength)
            {
                if (target.Cell is JsString str)
                    return str.Length;
            }
            else if (primitiveEntry.Kind == PropertyCacheKind.PrimitivePrototype && primitiveEntry.Epoch == InlineCache.Epoch
                && ReferenceEquals(GetPrimitivePrototypeOrNull(frame.Realm, target), primitiveEntry.Prototype))
            {
                return primitiveEntry.Holder!.GetValueForCache(primitiveEntry.Position, primitiveEntry.InTable);
            }
        }

        return GetPrimitivePropertyMiss(frame, name, cache, target);
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
        if (IsMegamorphic(frame, cache))
        {
            // Not cached
        }
        else if (target.Cell is JsString && key.Equals(LengthKey))
        {
            StoreCacheEntry(frame, cache, new PropertyCacheEntry { Kind = PropertyCacheKind.StringLength });
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
                        });
                    }

                    break;
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
        if (!IsMegamorphic(frame, cache) && CreateGetEntry(obj, key) is { } entry)
        {
            StoreCacheEntry(frame, cache, entry);
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
                case PropertyCacheKind.Add when ReferenceEquals(obj.PrototypeSlot, entry.Prototype) && entry.Epoch == InlineCache.Epoch && obj.IsExtensibleSlot:
                    obj.AddPropertyForCache(entry.NextShape!, value);
                    return;
                case PropertyCacheKind.OwnTable when entry.Epoch == InlineCache.Epoch:
                    obj.SetValueForCache(entry.Position, inTable: true, value);
                    return;
            }
        }

        SetPropertyMiss(frame, name, cache, target, value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetPropertyMiss(Frame frame, int name, int cache, JsValue target, JsValue value)
    {
        ref readonly var key = ref frame.Code.Keys[name];
        if (target.Cell is JsObject obj && !IsMegamorphic(frame, cache) && CreateSetEntry(obj, key) is { } entry)
        {
            StoreCacheEntry(frame, cache, entry);
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
        return new PropertyCacheEntry { Map = map, Kind = PropertyCacheKind.Add, Prototype = obj.PrototypeSlot, NextShape = next, Epoch = InlineCache.Epoch };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Object literals
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>CreateDataPropertyOrThrow of a property of an object literal.</summary>
    private static void DefineField(Frame frame, int name, int cache, JsValue target, JsValue value)
    {
        var obj = (JsObject)target.Cell!;
        if (frame.Code.Caches[cache] is { } entry && ReferenceEquals(obj.Map, entry.Map) && obj.IsExtensibleSlot)
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
        if (!obj.IsExotic && !IsMegamorphic(frame, cache) && obj.GetMapForCache(key) is Shape shape && shape.Find(key) < 0 && shape.AddProperty(key, PropertyFlags.Default) is { } next)
        {
            // CreateDataProperty does not look at the prototypes: the shape is enough
            StoreCacheEntry(frame, cache, new PropertyCacheEntry { Map = shape, Kind = PropertyCacheKind.Add, NextShape = next });
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
        var realm = frame.Realm;
        ref readonly var key = ref frame.Code.Keys[name];
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

        return LoadGlobal(realm, key, throwIfMissing);
    }
}
