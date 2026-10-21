using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The property accesses with an inline cache (<see cref="PropertyCacheEntry"/>): the fast path, then the lookup that fills the cache.</summary>
internal static partial class JsInterpreter
{
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

        return GetPropertyOfPrimitive(frame.Realm, target, frame.Code.Keys[name]);
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
        if (CreateGetEntry(obj, key) is { } entry)
        {
            frame.Code.Caches[cache] = entry;
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
                obj.MarkInlineCacheDependency();
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
            receiver.MarkInlineCacheDependency();
        }

        for (var prototype = receiver.PrototypeSlot; prototype is not null; prototype = prototype.PrototypeSlot)
        {
            prototype.MarkInlineCacheDependency();
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
        if (target.Cell is JsObject obj && CreateSetEntry(obj, key) is { } entry)
        {
            frame.Code.Caches[cache] = entry;
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

            obj.MarkInlineCacheDependency();
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
        if (!obj.IsExotic && obj.GetMapForCache(key) is Shape shape && shape.Find(key) < 0 && shape.AddProperty(key, PropertyFlags.Default) is { } next)
        {
            // CreateDataProperty does not look at the prototypes: the shape is enough
            frame.Code.Caches[cache] = new PropertyCacheEntry { Map = shape, Kind = PropertyCacheKind.Add, NextShape = next };
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
            else if (entry.Epoch == InlineCache.Epoch)
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
            // A data property of the global object (a new global lexical declaration invalidates the caches)
            var global = realm.GlobalObject;
            if (global.GetMapForCache(key) is { } map && global.TryFindOwnForCache(key, out var position, out var flags) && (flags & PropertyFlags.Accessor) == 0)
            {
                global.MarkInlineCacheDependency();
                frame.Code.Caches[cache] = new PropertyCacheEntry
                {
                    Kind = PropertyCacheKind.OwnTable,
                    Map = map,
                    Holder = global,
                    Position = position,
                    InTable = map is PropertyTable,
                    Epoch = InlineCache.Epoch,
                    Realm = realm,
                };
            }
        }

        return LoadGlobal(realm, key, throwIfMissing);
    }
}
