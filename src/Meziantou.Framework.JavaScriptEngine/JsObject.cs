using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript object. The internal methods (https://tc39.es/ecma262/#sec-object-internal-methods-and-internal-slots)
/// implement the ordinary object behavior; the exotic objects (arrays, proxies, arguments, typed arrays...) override them.
/// </summary>
public class JsObject : JsCell
{
    // Elements in which a hole is an absent property: the dense storage of the array indices (attributes: W/E/C)
    private JsValue[]? _elements;
    private PropertyTable? _properties;
    private JsObject? _prototype;

    internal JsObject(JsObject? prototype, ObjectClass objectClass = ObjectClass.Object)
        : base(JsValueKind.Object)
    {
        _prototype = prototype;
        Class = objectClass;
        Flags = ObjectFlags.Extensible;
    }

    internal ObjectFlags Flags;

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
        return true;
    }

    /// <summary>Sets the prototype without any check (for the objects being created).</summary>
    internal void SetPrototypeDirect(JsObject? prototype) => _prototype = prototype;

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

        if (_properties is { } properties && (!key.IsArrayIndex || (Flags & ObjectFlags.IndexedProperties) != 0))
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

    private static PropertyDescriptor GetDescriptor(PropertyTable properties, int position)
    {
        var flags = properties.GetFlags(position);
        ref var value = ref properties.GetValue(position);
        if ((flags & PropertyFlags.Accessor) != 0)
        {
            var pair = (AccessorPair)value.Cell!;
            return PropertyDescriptor.Accessor(pair.Getter, pair.Setter, flags);
        }

        if ((flags & PropertyFlags.Lazy) != 0)
        {
            value = ((LazyValue)value.Cell!).Evaluate();
            flags &= ~PropertyFlags.Lazy;
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

        if (_properties is { } properties && (!key.IsArrayIndex || (Flags & ObjectFlags.IndexedProperties) != 0))
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

            if (_properties is { } table && (Flags & ObjectFlags.IndexedProperties) != 0)
            {
                var existing = table.Find(key);
                if (existing >= 0)
                {
                    table.GetValue(existing) = value;
                    table.GetFlags(existing) = flags;
                    return;
                }
            }

            Flags |= ObjectFlags.IndexedProperties;
        }

        var properties = _properties ??= new PropertyTable();
        var position = properties.Find(key);
        if (position >= 0)
        {
            properties.GetValue(position) = value;
            properties.GetFlags(position) = flags;
        }
        else
        {
            properties.Add(key, value, flags);
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
        if (index > (uint)currentLength + 1024 && index > (uint)currentLength * 2)
            return false;

        var newLength = Math.Max(Math.Max(currentLength * 2, (int)index + 1), 4);
        var newElements = new JsValue[newLength];
        if (elements is not null)
        {
            Array.Copy(elements, newElements, currentLength);
        }

        newElements.AsSpan(currentLength).Fill(JsValue.Hole);
        newElements[index] = value;
        _elements = newElements;
        return true;
    }

    /// <summary>Removes an own property (no check).</summary>
    internal void RemoveOwnSlot(in PropertyKey key)
    {
        if (key.IsArrayIndex && _elements is { } elements && key.ArrayIndex < (uint)elements.Length && !elements[key.ArrayIndex].IsHole)
        {
            elements[key.ArrayIndex] = JsValue.Hole;
            return;
        }

        if (_properties is { } properties)
        {
            var position = properties.Find(key);
            if (position >= 0)
            {
                properties.Remove(position);
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

        if (_properties is { } properties)
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
    internal bool CreateDataProperty(in PropertyKey key, JsValue value) => DefineOwnProperty(key, PropertyDescriptor.Data(value));

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
        SetOwnSlot(key, new JsValue(new AccessorPair(getter is null ? default : new JsValue(getter), setter is null ? default : new JsValue(setter))), (flags & (PropertyFlags.Enumerable | PropertyFlags.Configurable)) | PropertyFlags.Accessor);
    }

    /// <summary>Defines a data property whose value is computed the first time it is read.</summary>
    internal void DefineLazy(in PropertyKey key, Func<JsValue> factory, PropertyFlags flags = PropertyFlags.BuiltIn)
    {
        SetOwnSlot(key, new JsValue(new LazyValue(factory)), (flags & PropertyFlags.Default) | PropertyFlags.Lazy);
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
        if (_properties is not { } properties || (Flags & ObjectFlags.IndexedProperties) == 0)
            yield break;

        for (var i = 0; i < properties.Capacity; i++)
        {
            if (!properties.IsDeleted(i) && properties.GetKey(i).IsArrayIndex)
                yield return properties.GetKey(i);
        }
    }
}
