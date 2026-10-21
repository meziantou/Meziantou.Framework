using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript object. The internal methods (https://tc39.es/ecma262/#sec-object-internal-methods-and-internal-slots)
/// implement the ordinary object behavior; the exotic objects (arrays, proxies, arguments, typed arrays...) override them.
/// </summary>
public partial class JsObject : JsCell
{
    // Elements in which a hole is an absent property: the dense storage of the array indices (attributes: W/E/C)
    private JsValue[]? _elements;

    // The named properties (and the array indices that are not in the elements): a shape shared with other objects and the
    // values in its slots, or a property table of the object (dictionary mode, _slots is null). Null: an exotic object
    // without properties yet (its named properties may not be ordinary).
    private PropertyMap? _map;
    private JsValue[]? _slots;
    private ObjectFlags _flags;
    private JsObject? _prototype;

    internal JsObject(JsObject? prototype, ObjectClass objectClass = ObjectClass.Object)
        : base(JsValueKind.Object)
    {
        _prototype = prototype;
        Class = objectClass;
        _flags = ObjectFlags.Extensible;
        _map = Shape.EmptyOrdinary;
    }

    internal ObjectFlags Flags
    {
        get => _flags;
        set
        {
            _flags = value;

            // An exotic object does not share the shapes of the ordinary objects: the inline caches would skip its internal methods
            if ((value & ObjectFlags.Exotic) != 0 && ReferenceEquals(_map, Shape.EmptyOrdinary))
            {
                _map = GetEmptyShape();
            }
        }
    }

    internal ObjectClass Class { get; set; }

    /// <summary>The private elements of the object (fields, methods and accessors whose name is a private name).</summary>
    internal PrivateElementTable? PrivateElements { get; set; }

    /// <summary>Gets the prototype without running code ([[Prototype]], not [[GetPrototypeOf]]).</summary>
    internal JsObject? PrototypeSlot => _prototype;

    internal bool IsExtensibleSlot => (Flags & ObjectFlags.Extensible) != 0;

    internal bool IsExotic => (Flags & ObjectFlags.Exotic) != 0;

    /// <summary>Gets whether the object has a [[Call]] internal method.</summary>
    public bool IsCallable => (Flags & ObjectFlags.Callable) != 0;

    /// <summary>Gets whether the object has a [[Construct]] internal method.</summary>
    public bool IsConstructor => (Flags & ObjectFlags.Constructor) != 0;

    internal bool IsHtmlDda => (Flags & ObjectFlags.IsHtmlDda) != 0;

    /// <summary>Gets a name of the kind of object, for debugging.</summary>
    internal virtual string ClassName => Class.ToString();

    // ------------------------------------------------------------------------------------------------------------------
    // Internal methods
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>[[GetPrototypeOf]]</summary>
    internal virtual JsObject? GetPrototypeOf() => _prototype;

    /// <summary>[[SetPrototypeOf]]</summary>
    internal virtual bool SetPrototypeOf(JsObject? prototype) => OrdinarySetPrototypeOf(prototype);

    /// <summary>[[IsExtensible]]</summary>
    internal virtual bool IsExtensible() => IsExtensibleSlot;

    /// <summary>[[PreventExtensions]]</summary>
    internal virtual bool PreventExtensions()
    {
        Flags &= ~ObjectFlags.Extensible;
        return true;
    }

    /// <summary>[[GetOwnProperty]]</summary>
    internal virtual bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor) => OrdinaryGetOwnProperty(key, out descriptor);

    /// <summary>[[DefineOwnProperty]]</summary>
    internal virtual bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor) => OrdinaryDefineOwnProperty(key, descriptor);

    /// <summary>[[HasProperty]]</summary>
    internal virtual bool HasProperty(in PropertyKey key) => OrdinaryHasProperty(key);

    /// <summary>[[Get]]</summary>
    internal virtual JsValue Get(in PropertyKey key, JsValue receiver) => OrdinaryGet(key, receiver);

    /// <summary>[[Set]]</summary>
    internal virtual bool Set(in PropertyKey key, JsValue value, JsValue receiver) => OrdinarySet(key, value, receiver);

    /// <summary>[[Delete]]</summary>
    internal virtual bool Delete(in PropertyKey key) => OrdinaryDelete(key);

    /// <summary>[[OwnPropertyKeys]]</summary>
    internal virtual List<PropertyKey> OwnPropertyKeys() => OrdinaryOwnPropertyKeys();

    /// <summary>[[Call]]</summary>
    internal virtual JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        ThrowHelper.ThrowTypeError("object is not a function");
        return default;
    }

    /// <summary>[[Construct]]</summary>
    internal virtual JsValue Construct(ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        ThrowHelper.ThrowTypeError("object is not a constructor");
        return default;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Ordinary internal methods (https://tc39.es/ecma262/#sec-ordinary-object-internal-methods-and-internal-slots)
    // ------------------------------------------------------------------------------------------------------------------

    internal bool OrdinarySetPrototypeOf(JsObject? prototype)
    {
        if (ReferenceEquals(prototype, _prototype))
            return true;

        if ((Flags & ObjectFlags.ImmutablePrototype) != 0)
            return false;

        if (!IsExtensibleSlot)
            return false;

        // Prevent cycles; stop at an object whose [[GetPrototypeOf]] is not the ordinary one (a proxy)
        for (var p = prototype; p is not null; p = p._prototype)
        {
            if (ReferenceEquals(p, this))
                return false;

            if (p.Class == ObjectClass.Proxy)
                break;
        }

        _prototype = prototype;
        OnPrototypeChanged();
        return true;
    }

    /// <summary>Sets the prototype without any check (for the objects being created).</summary>
    internal void SetPrototypeDirect(JsObject? prototype)
    {
        _prototype = prototype;
        OnPrototypeChanged();
    }

    internal bool OrdinaryGetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (key.IsArrayIndex && _elements is { } elements && key.ArrayIndex < (uint)elements.Length)
        {
            ref var value = ref elements[key.ArrayIndex];
            if (!value.IsHole)
            {
                descriptor = PropertyDescriptor.Data(value);
                return true;
            }
        }

        if (_map is Shape shape)
        {
            var slot = shape.Find(key);
            if (slot >= 0)
            {
                var flags = shape.GetFlags(slot);
                var value = _slots![slot];
                if ((flags & PropertyFlags.Accessor) != 0)
                {
                    var pair = (AccessorPair)value.Cell!;
                    descriptor = PropertyDescriptor.Accessor(pair.Getter, pair.Setter, flags);
                }
                else
                {
                    descriptor = PropertyDescriptor.Data(value, flags);
                }

                return true;
            }
        }
        else if (_map is PropertyTable properties && (!key.IsArrayIndex || (Flags & ObjectFlags.IndexedProperties) != 0))
        {
            var position = properties.Find(key);
            if (position >= 0)
            {
                descriptor = GetDescriptor(properties, position);
                return true;
            }
        }

        descriptor = default;
        return false;
    }

    private PropertyDescriptor GetDescriptor(PropertyTable properties, int position)
    {
        var flags = properties.GetFlags(position);
        ref var value = ref properties.GetValue(position);
        if ((flags & PropertyFlags.Accessor) != 0)
        {
            var pair = (AccessorPair)value.Cell!;
            return PropertyDescriptor.Accessor(pair.Getter, pair.Setter, flags);
        }

        if ((flags & PropertyFlags.Lazy) != 0)
            return EvaluateLazyProperty(properties, position, flags);

        return PropertyDescriptor.Data(value, flags);
    }

    private static PropertyDescriptor EvaluateLazyProperty(PropertyTable properties, int position, PropertyFlags flags)
    {
        var lazy = (LazyCell)properties.GetValue(position).Cell!;
        var key = properties.GetKey(position);
        var value = lazy.Evaluate();
        flags &= ~PropertyFlags.Lazy;

        // The evaluation can change the object (e.g. add properties, which can move the entries): the property is found
        // again. The inline caches are not invalidated: they never remember a lazy property (they evaluate it first), and its
        // attributes do not change (the built-in methods are evaluated on first access, while the caches are warm).
        position = properties.Find(key);
        if (position >= 0 && ReferenceEquals(properties.GetValue(position).Cell, lazy))
        {
            properties.GetValue(position) = value;
            properties.GetFlags(position) = flags;
        }

        return PropertyDescriptor.Data(value, flags);
    }

    /// <summary>Finds an own data or accessor property without running code: the fast path of [[Get]] for the ordinary objects.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetOwnSlot(in PropertyKey key, out JsValue value, out PropertyFlags flags)
    {
        if (key.IsArrayIndex && _elements is { } elements && key.ArrayIndex < (uint)elements.Length)
        {
            value = elements[key.ArrayIndex];
            if (!value.IsHole)
            {
                flags = PropertyFlags.Default;
                return true;
            }
        }

        var map = _map;
        if (map is Shape shape)
        {
            var slot = shape.Find(key);
            if (slot >= 0)
            {
                flags = shape.GetFlags(slot);
                value = _slots![slot];
                return true;
            }
        }
        else if (map is PropertyTable properties && (!key.IsArrayIndex || (Flags & ObjectFlags.IndexedProperties) != 0))
        {
            var position = properties.Find(key);
            if (position >= 0)
            {
                flags = properties.GetFlags(position);
                if ((flags & PropertyFlags.Lazy) != 0)
                {
                    GetDescriptor(properties, position);
                    flags = properties.GetFlags(position);
                }

                value = properties.GetValue(position);
                return true;
            }
        }

        value = default;
        flags = default;
        return false;
    }

    internal bool OrdinaryDefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        var exists = GetOwnProperty(key, out var current);
        return ValidateAndApplyPropertyDescriptor(this, key, IsExtensible(), descriptor, exists, current);
    }

    /// <summary>ValidateAndApplyPropertyDescriptor (https://tc39.es/ecma262/#sec-validateandapplypropertydescriptor).</summary>
    internal static bool ValidateAndApplyPropertyDescriptor(JsObject? obj, in PropertyKey key, bool extensible, in PropertyDescriptor desc, bool hasCurrent, in PropertyDescriptor current)
    {
        if (!hasCurrent)
        {
            if (!extensible)
                return false;

            if (obj is null)
                return true;

            if (desc.IsAccessorDescriptor)
            {
                var flags = PropertyFlags.Accessor;
                if (desc.Enumerable)
                {
                    flags |= PropertyFlags.Enumerable;
                }

                if (desc.Configurable)
                {
                    flags |= PropertyFlags.Configurable;
                }

                obj.SetOwnSlot(key, new JsValue(new AccessorPair(desc.Get, desc.Set)), flags);
            }
            else
            {
                obj.SetOwnSlot(key, desc.Value, desc.Flags & PropertyFlags.Default);
            }

            return true;
        }

        if (desc.Fields == DescriptorFields.None)
            return true;

        if (!current.Configurable)
        {
            if (desc.HasConfigurable && desc.Configurable)
                return false;

            if (desc.HasEnumerable && desc.Enumerable != current.Enumerable)
                return false;

            if (!desc.IsGenericDescriptor && desc.IsAccessorDescriptor != current.IsAccessorDescriptor)
                return false;

            if (current.IsAccessorDescriptor)
            {
                if (desc.HasGet && !JsOperations.SameValue(desc.Get, current.Get))
                    return false;

                if (desc.HasSet && !JsOperations.SameValue(desc.Set, current.Set))
                    return false;
            }
            else if (!current.Writable)
            {
                if (desc.HasWritable && desc.Writable)
                    return false;

                if (desc.HasValue && !JsOperations.SameValue(desc.Value, current.Value))
                    return false;
            }
        }

        if (obj is null)
            return true;

        // Apply the changes
        var enumerable = desc.HasEnumerable ? desc.Enumerable : current.Enumerable;
        var configurable = desc.HasConfigurable ? desc.Configurable : current.Configurable;
        var newFlags = (enumerable ? PropertyFlags.Enumerable : 0) | (configurable ? PropertyFlags.Configurable : 0);
        if (current.IsDataDescriptor && desc.IsAccessorDescriptor)
        {
            obj.SetOwnSlot(key, new JsValue(new AccessorPair(desc.HasGet ? desc.Get : default, desc.HasSet ? desc.Set : default)), newFlags | PropertyFlags.Accessor);
        }
        else if (current.IsAccessorDescriptor && desc.IsDataDescriptor)
        {
            if (desc.HasWritable && desc.Writable)
            {
                newFlags |= PropertyFlags.Writable;
            }

            obj.SetOwnSlot(key, desc.HasValue ? desc.Value : default, newFlags);
        }
        else if (current.IsAccessorDescriptor)
        {
            var getter = desc.HasGet ? desc.Get : current.Get;
            var setter = desc.HasSet ? desc.Set : current.Set;
            obj.SetOwnSlot(key, new JsValue(new AccessorPair(getter, setter)), newFlags | PropertyFlags.Accessor);
        }
        else
        {
            var writable = desc.HasWritable ? desc.Writable : current.Writable;
            if (writable)
            {
                newFlags |= PropertyFlags.Writable;
            }

            obj.SetOwnSlot(key, desc.HasValue ? desc.Value : current.Value, newFlags);
        }

        return true;
    }

    /// <summary>Creates or replaces an own property (no check, no exotic behavior).</summary>
    internal void SetOwnSlot(in PropertyKey key, JsValue value, PropertyFlags flags)
    {
        if (key.IsArrayIndex)
        {
            var index = key.ArrayIndex;
            if (flags == PropertyFlags.Default && (Flags & ObjectFlags.IndexedProperties) == 0)
            {
                if (TrySetElement(index, value))
                    return;
            }
            else if (_elements is { } elements && index < (uint)elements.Length && !elements[index].IsHole)
            {
                // The property moves from the dense elements to the property table
                elements[index] = JsValue.Hole;
            }

            // The array indices that are not in the elements are in a property table
            var indexTable = GetPropertyTable();
            if ((Flags & ObjectFlags.IndexedProperties) != 0)
            {
                var existing = indexTable.Find(key);
                if (existing >= 0)
                {
                    SetTableEntry(indexTable, existing, value, flags);
                    return;
                }
            }

            Flags |= ObjectFlags.IndexedProperties;
            var indexCompactions = indexTable.Compactions;
            indexTable.Add(key, value, flags);
            OnPropertyAdded(indexTable.Compactions != indexCompactions);
            return;
        }

        if (_map is PropertyTable properties)
        {
            var position = properties.Find(key);
            if (position >= 0)
            {
                SetTableEntry(properties, position, value, flags);
            }
            else
            {
                var compactions = properties.Compactions;
                properties.Add(key, value, flags);
                OnPropertyAdded(properties.Compactions != compactions);
            }

            return;
        }

        var shape = (Shape?)_map;
        var slot = shape is null ? -1 : shape.Find(key);
        if (slot < 0)
        {
            AddProperty(key, value, flags);
        }
        else if (shape!.GetFlags(slot) == flags)
        {
            _slots![slot] = value;
        }
        else
        {
            // A reconfigured property: the object leaves the shared shapes
            var table = GetPropertyTable();
            SetTableEntry(table, table.Find(key), value, flags);
        }
    }

    private void SetTableEntry(PropertyTable table, int position, JsValue value, PropertyFlags flags)
    {
        table.GetValue(position) = value;
        ref var currentFlags = ref table.GetFlags(position);
        if (currentFlags != flags)
        {
            currentFlags = flags;
            OnStructureChanged();
        }
    }

    /// <summary>Adds a named property that does not exist: in the next shape, or in the property table.</summary>
    private void AddProperty(in PropertyKey key, JsValue value, PropertyFlags flags)
    {
        if (_map is not PropertyTable && (flags & PropertyFlags.Lazy) == 0)
        {
            var shape = (Shape?)_map ?? GetEmptyShape();
            if (shape?.AddProperty(key, flags) is { } next)
            {
                var slot = shape.Count;
                var slots = _slots;
                if (slots is null)
                {
                    _slots = slots = new JsValue[next.GetSlotsLength(0)];
                }
                else if (slot >= slots.Length)
                {
                    Array.Resize(ref _slots, next.GetSlotsLength(slots.Length));
                    slots = _slots;
                }

                slots[slot] = value;
                _map = next;
                OnPropertyAdded(positionsChanged: false);
                return;
            }
        }

        var table = GetPropertyTable();
        var tableCompactions = table.Compactions;
        table.Add(key, value, flags);
        OnPropertyAdded(table.Compactions != tableCompactions);
    }

    /// <summary>Gets the empty shape of the kind of the object, or null when its named properties are exotic (it uses a property table).</summary>
    private Shape? GetEmptyShape()
    {
        if (!IsExotic)
            return Shape.EmptyOrdinary;

        return this switch
        {
            JsArray => Shape.EmptyArray,
            JsStringObject or Interpreter.JsArgumentsObject => Shape.EmptyIndexedExotic,
            _ => null,
        };
    }

    /// <summary>Gets the property table of the object: the properties of its shape move to a table of its own (dictionary mode).</summary>
    private PropertyTable GetPropertyTable()
    {
        if (_map is PropertyTable existing)
            return existing;

        var shape = (Shape?)_map;
        var count = shape?.Count ?? 0;
        var table = new PropertyTable(Math.Max(4, count));
        for (var i = 0; i < count; i++)
        {
            table.Add(shape!.GetKey(i), _slots![i], shape.GetFlags(i));
        }

        _map = table;
        _slots = null;
        OnStructureChanged();
        return table;
    }

    /// <summary>Invalidates the inline caches that depend on the object: a property was removed or reconfigured, or the properties moved.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnStructureChanged()
    {
        if ((Flags & (ObjectFlags.InlineCacheHolder | ObjectFlags.InlineCachePrototype)) != 0)
        {
            InlineCache.Invalidate();
        }
    }

    /// <summary>
    /// Invalidates the inline caches that depend on the absence of a property in the object (a prototype): a property was
    /// added. The caches that only remember the positions of its properties are still valid (unless they moved).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnPropertyAdded(bool positionsChanged)
    {
        var flags = Flags;
        if ((flags & ObjectFlags.InlineCachePrototype) != 0 || (positionsChanged && (flags & ObjectFlags.InlineCacheHolder) != 0))
        {
            InlineCache.Invalidate();
        }
    }

    /// <summary>Invalidates the inline caches whose lookups go through the object (a prototype).</summary>
    private void OnPrototypeChanged()
    {
        if ((Flags & ObjectFlags.InlineCachePrototype) != 0)
        {
            InlineCache.Invalidate();
        }
    }

    /// <summary>Stores an element in the dense storage, growing it when the index is close to the end; false when it is too sparse.</summary>
    private bool TrySetElement(uint index, JsValue value)
    {
        var elements = _elements;
        if (elements is not null && index < (uint)elements.Length)
        {
            elements[index] = value;
            return true;
        }

        var currentLength = elements?.Length ?? 0;
        if (index >= (uint)Array.MaxLength / 2)
            return false;

        // Close to the end: grow. Further away (up to twice the current length): grow only when the current storage is
        // at least half full, so that a sequence of writes at exponentially increasing indices stays sparse.
        if (index > (uint)currentLength + 1024)
        {
            if (index > (uint)currentLength * 2 || !IsMostlyFilled(elements))
                return false;
        }

        var newLength = (int)Math.Min(Math.Max(Math.Max((long)currentLength * 2, (long)index + 1), 4), Array.MaxLength);
        var newElements = new JsValue[newLength];
        if (elements is not null)
        {
            Array.Copy(elements, newElements, currentLength);
        }

        newElements.AsSpan(currentLength).Fill(JsValue.Hole);
        newElements[index] = value;
        _elements = newElements;
        return true;

        static bool IsMostlyFilled(JsValue[]? elements)
        {
            if (elements is null)
                return false;

            var holes = 0;
            var maxHoles = elements.Length / 2;
            foreach (var element in elements)
            {
                if (element.IsHole && ++holes > maxHoles)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Removes an own property (no check).</summary>
    internal void RemoveOwnSlot(in PropertyKey key)
    {
        if (key.IsArrayIndex && _elements is { } elements && key.ArrayIndex < (uint)elements.Length && !elements[key.ArrayIndex].IsHole)
        {
            elements[key.ArrayIndex] = JsValue.Hole;
            return;
        }

        if (_map is Shape shape)
        {
            var slot = shape.Find(key);
            if (slot < 0)
                return;

            if (slot == shape.Count - 1 && shape.Parent is { } parent)
            {
                // The last property: back to the parent shape
                _slots![slot] = default;
                _map = parent;
                OnStructureChanged();
                return;
            }
        }

        if (_map is not null)
        {
            var properties = GetPropertyTable();
            var position = properties.Find(key);
            if (position >= 0)
            {
                properties.Remove(position);
                OnStructureChanged();
            }
        }
    }

    internal bool OrdinaryHasProperty(in PropertyKey key)
    {
        var obj = this;
        while (true)
        {
            if (obj.IsExotic)
                return obj.HasPropertyExoticStep(key);

            if (obj.TryGetOwnSlot(key, out _, out _))
                return true;

            var parent = obj.GetPrototypeOf();
            if (parent is null)
                return false;

            if (parent.IsExotic)
                return parent.HasProperty(key);

            obj = parent;
        }
    }

    private bool HasPropertyExoticStep(in PropertyKey key)
    {
        if (GetOwnProperty(key, out _))
            return true;

        var parent = GetPrototypeOf();
        return parent is not null && parent.HasProperty(key);
    }

    internal JsValue OrdinaryGet(in PropertyKey key, JsValue receiver)
    {
        var obj = this;
        while (true)
        {
            if (obj.IsExotic && !ReferenceEquals(obj, this))
                return obj.Get(key, receiver);

            JsValue value;
            PropertyFlags flags;
            bool found;
            if (obj.IsExotic)
            {
                found = obj.GetOwnProperty(key, out var descriptor);
                if (found)
                {
                    if (descriptor.IsAccessorDescriptor)
                        return descriptor.Get.IsUndefined ? JsValue.Undefined : descriptor.Get.AsObject().Call(receiver, []);

                    return descriptor.Value;
                }
            }
            else
            {
                found = obj.TryGetOwnSlot(key, out value, out flags);
                if (found)
                {
                    if ((flags & PropertyFlags.Accessor) != 0)
                    {
                        var getter = ((AccessorPair)value.Cell!).Getter;
                        return getter.IsUndefined ? JsValue.Undefined : getter.AsObject().Call(receiver, []);
                    }

                    return value;
                }
            }

            var parent = obj.GetPrototypeOf();
            if (parent is null)
                return JsValue.Undefined;

            obj = parent;
        }
    }

    internal bool OrdinarySet(in PropertyKey key, JsValue value, JsValue receiver)
    {
        // Fast path: an own writable data property of an ordinary receiver
        if (!IsExotic && receiver.Cell == this && TryGetOwnSlot(key, out _, out var ownFlags) && (ownFlags & (PropertyFlags.Accessor | PropertyFlags.Writable)) == PropertyFlags.Writable)
        {
            SetOwnSlot(key, value, ownFlags);
            return true;
        }

        if (!GetOwnProperty(key, out var ownDesc))
        {
            var parent = GetPrototypeOf();
            if (parent is not null)
                return parent.Set(key, value, receiver);

            ownDesc = PropertyDescriptor.Data(JsValue.Undefined);
        }

        return OrdinarySetWithOwnDescriptor(key, value, receiver, ownDesc);
    }

    /// <summary>OrdinarySetWithOwnDescriptor (https://tc39.es/ecma262/#sec-ordinarysetwithowndescriptor).</summary>
    internal static bool OrdinarySetWithOwnDescriptor(in PropertyKey key, JsValue value, JsValue receiver, in PropertyDescriptor ownDesc)
    {
        if (ownDesc.IsDataDescriptor)
        {
            if (!ownDesc.Writable)
                return false;

            if (receiver.Cell is not JsObject receiverObject)
                return false;

            if (receiverObject.GetOwnProperty(key, out var existing))
            {
                if (existing.IsAccessorDescriptor)
                    return false;

                if (!existing.Writable)
                    return false;

                return receiverObject.DefineOwnProperty(key, PropertyDescriptor.ValueOnly(value));
            }

            return receiverObject.DefineOwnProperty(key, PropertyDescriptor.Data(value));
        }

        var setter = ownDesc.Set;
        if (setter.IsUndefined)
            return false;

        setter.AsObject().Call(receiver, [value]);
        return true;
    }

    internal bool OrdinaryDelete(in PropertyKey key)
    {
        if (!GetOwnProperty(key, out var descriptor))
            return true;

        if (descriptor.Configurable)
        {
            RemoveOwnSlot(key);
            return true;
        }

        return false;
    }

    internal List<PropertyKey> OrdinaryOwnPropertyKeys()
    {
        var keys = new List<PropertyKey>();
        if (_elements is { } elements)
        {
            for (var i = 0; i < elements.Length; i++)
            {
                if (!elements[i].IsHole)
                {
                    keys.Add(new PropertyKey((uint)i));
                }
            }
        }

        if (_map is Shape shape)
        {
            // No array index in a shape: the strings, then the symbols, in insertion order
            var hasSymbols = false;
            for (var i = 0; i < shape.Count; i++)
            {
                var key = shape.GetKey(i);
                if (key.IsSymbol)
                {
                    hasSymbols = true;
                }
                else
                {
                    keys.Add(key);
                }
            }

            if (hasSymbols)
            {
                for (var i = 0; i < shape.Count; i++)
                {
                    var key = shape.GetKey(i);
                    if (key.IsSymbol)
                    {
                        keys.Add(key);
                    }
                }
            }
        }
        else if (_map is PropertyTable properties)
        {
            if ((Flags & ObjectFlags.IndexedProperties) != 0)
            {
                var start = keys.Count;
                for (var i = 0; i < properties.Capacity; i++)
                {
                    if (!properties.IsDeleted(i) && properties.GetKey(i).IsArrayIndex)
                    {
                        keys.Add(properties.GetKey(i));
                    }
                }

                if (keys.Count > start)
                {
                    keys.Sort(static (a, b) => a.ArrayIndex.CompareTo(b.ArrayIndex));
                }
            }

            for (var i = 0; i < properties.Capacity; i++)
            {
                if (!properties.IsDeleted(i))
                {
                    var key = properties.GetKey(i);
                    if (!key.IsArrayIndex && key.IsString)
                    {
                        keys.Add(key);
                    }
                }
            }

            for (var i = 0; i < properties.Capacity; i++)
            {
                if (!properties.IsDeleted(i) && properties.GetKey(i).IsSymbol)
                {
                    keys.Add(properties.GetKey(i));
                }
            }
        }

        return keys;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Helpers (https://tc39.es/ecma262/#sec-operations-on-objects)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Get(O, P)</summary>
    internal JsValue Get(in PropertyKey key) => Get(key, new JsValue(this));

    /// <summary>Set(O, P, V, Throw)</summary>
    internal void Set(in PropertyKey key, JsValue value, bool throwOnFailure)
    {
        if (!Set(key, value, new JsValue(this)) && throwOnFailure)
        {
            ThrowHelper.ThrowTypeError($"Cannot assign to read only property '{key}' of object");
        }
    }

    /// <summary>CreateDataProperty(O, P, V)</summary>
    internal bool CreateDataProperty(in PropertyKey key, JsValue value)
    {
        // Fast path: a new or configurable data property of an ordinary extensible object (e.g. an object literal)
        if (!IsExotic && !key.IsArrayIndex && IsExtensibleSlot)
        {
            if (_map is PropertyTable properties)
            {
                var position = properties.Find(key);
                if (position < 0)
                {
                    var compactions = properties.Compactions;
                    properties.Add(key, value, PropertyFlags.Default);
                    OnPropertyAdded(properties.Compactions != compactions);
                    return true;
                }

                if ((properties.GetFlags(position) & PropertyFlags.Configurable) != 0)
                {
                    SetTableEntry(properties, position, value, PropertyFlags.Default);
                    return true;
                }
            }
            else
            {
                var shape = (Shape?)_map;
                var slot = shape is null ? -1 : shape.Find(key);
                if (slot < 0)
                {
                    AddProperty(key, value, PropertyFlags.Default);
                    return true;
                }

                if ((shape!.GetFlags(slot) & PropertyFlags.Configurable) != 0)
                {
                    SetOwnSlot(key, value, PropertyFlags.Default);
                    return true;
                }
            }
        }

        return DefineOwnProperty(key, PropertyDescriptor.Data(value));
    }

    /// <summary>CreateDataPropertyOrThrow(O, P, V)</summary>
    internal void CreateDataPropertyOrThrow(in PropertyKey key, JsValue value)
    {
        if (!CreateDataProperty(key, value))
        {
            ThrowHelper.ThrowTypeError($"Cannot define property '{key}'");
        }
    }

    /// <summary>DefinePropertyOrThrow(O, P, desc)</summary>
    internal void DefinePropertyOrThrow(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        if (!DefineOwnProperty(key, descriptor))
        {
            ThrowHelper.ThrowTypeError($"Cannot redefine property: {key}");
        }
    }

    /// <summary>DeletePropertyOrThrow(O, P)</summary>
    internal void DeletePropertyOrThrow(in PropertyKey key)
    {
        if (!Delete(key))
        {
            ThrowHelper.ThrowTypeError($"Cannot delete property '{key}'");
        }
    }

    /// <summary>HasOwnProperty(O, P)</summary>
    internal bool HasOwnProperty(in PropertyKey key) => GetOwnProperty(key, out _);

    /// <summary>Defines a built-in data property (writable, configurable, not enumerable by default) without any check.</summary>
    internal void DefineBuiltIn(in PropertyKey key, JsValue value, PropertyFlags flags = PropertyFlags.BuiltIn) => SetOwnSlot(key, value, flags);

    /// <summary>Defines a built-in accessor property without any check.</summary>
    internal void DefineBuiltInAccessor(in PropertyKey key, JsObject? getter, JsObject? setter, PropertyFlags flags = PropertyFlags.Configurable)
    {
        DefineBuiltInAccessor(key, new AccessorPair(getter is null ? default : new JsValue(getter), setter is null ? default : new JsValue(setter)), flags);
    }

    /// <summary>Defines a built-in accessor property (e.g. a lazy accessor) without any check.</summary>
    internal void DefineBuiltInAccessor(in PropertyKey key, AccessorPair accessor, PropertyFlags flags = PropertyFlags.Configurable)
    {
        SetOwnSlot(key, new JsValue(accessor), (flags & (PropertyFlags.Enumerable | PropertyFlags.Configurable)) | PropertyFlags.Accessor);
    }

    /// <summary>Defines a data property whose value is computed the first time it is read.</summary>
    internal void DefineLazy(in PropertyKey key, Func<JsValue> factory, PropertyFlags flags = PropertyFlags.BuiltIn) => DefineLazy(key, new LazyValue(factory), flags);

    internal void DefineLazy(in PropertyKey key, Func<object?, JsValue> factory, object? state, PropertyFlags flags) => DefineLazy(key, new LazyValue(factory, state), flags);

    internal void DefineLazy(in PropertyKey key, LazyCell value, PropertyFlags flags)
    {
        SetOwnSlot(key, new JsValue(value), (flags & PropertyFlags.Default) | PropertyFlags.Lazy);
    }

    /// <summary>Gets the dense elements (the array fast paths); the holes are absent properties.</summary>
    internal JsValue[]? DenseElements => _elements;

    /// <summary>Gets whether the array indices of the object are all in the dense elements.</summary>
    internal bool HasOnlyDenseElements => (Flags & ObjectFlags.IndexedProperties) == 0;

    /// <summary>Truncates the dense elements (the length of an array decreased).</summary>
    internal void TruncateElements(uint length)
    {
        if (_elements is { } elements && length < (uint)elements.Length)
        {
            elements.AsSpan((int)length).Fill(JsValue.Hole);
        }
    }

    /// <summary>Gets the own property keys that are array indices and are in the property table.</summary>
    internal IEnumerable<PropertyKey> GetIndexedTableKeys()
    {
        if (_map is not PropertyTable properties || (Flags & ObjectFlags.IndexedProperties) == 0)
            yield break;

        for (var i = 0; i < properties.Capacity; i++)
        {
            if (!properties.IsDeleted(i) && properties.GetKey(i).IsArrayIndex)
                yield return properties.GetKey(i);
        }
    }
}
