namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of the result of EnumerableOwnProperties.</summary>
internal enum EnumerableOwnPropertiesKind
{
    Key,
    Value,
    KeyValue,
}

/// <summary>The integrity levels of SetIntegrityLevel and TestIntegrityLevel.</summary>
internal enum IntegrityLevel
{
    Sealed,
    Frozen,
}

/// <summary>
/// The abstract operations on property descriptors and on the properties of objects shared by the built-ins and the
/// compiler: FromPropertyDescriptor, ToPropertyDescriptor, CompletePropertyDescriptor, SetIntegrityLevel,
/// TestIntegrityLevel, EnumerableOwnProperties, CreateListFromArrayLike, CopyDataProperties...
/// </summary>
internal static class PropertyOperations
{
    /// <summary>The maximum number of elements of a list created by CreateListFromArrayLike (the arguments of a call).</summary>
    public const int MaxListLength = 1 << 24;

    private static readonly PropertyKey ValueKey = new("value");
    private static readonly PropertyKey WritableKey = new("writable");
    private static readonly PropertyKey GetKey = new("get");
    private static readonly PropertyKey SetKey = new("set");
    private static readonly PropertyKey EnumerableKey = new("enumerable");
    private static readonly PropertyKey ConfigurableKey = new("configurable");

    /// <summary>FromPropertyDescriptor(Desc): an ordinary object of the realm with the fields of the descriptor.</summary>
    public static JsObject FromPropertyDescriptor(JsRealm realm, in PropertyDescriptor descriptor)
    {
        var obj = new JsObject(realm.Intrinsics.ObjectPrototype);
        if (descriptor.HasValue)
        {
            obj.SetOwnSlot(ValueKey, descriptor.Value, PropertyFlags.Default);
        }

        if (descriptor.HasWritable)
        {
            obj.SetOwnSlot(WritableKey, descriptor.Writable, PropertyFlags.Default);
        }

        if (descriptor.HasGet)
        {
            obj.SetOwnSlot(GetKey, descriptor.Get, PropertyFlags.Default);
        }

        if (descriptor.HasSet)
        {
            obj.SetOwnSlot(SetKey, descriptor.Set, PropertyFlags.Default);
        }

        if (descriptor.HasEnumerable)
        {
            obj.SetOwnSlot(EnumerableKey, descriptor.Enumerable, PropertyFlags.Default);
        }

        if (descriptor.HasConfigurable)
        {
            obj.SetOwnSlot(ConfigurableKey, descriptor.Configurable, PropertyFlags.Default);
        }

        return obj;
    }

    /// <summary>FromPropertyDescriptor(Desc) of an optional descriptor: undefined when there is no descriptor.</summary>
    public static JsValue FromPropertyDescriptor(JsRealm realm, bool hasDescriptor, in PropertyDescriptor descriptor)
    {
        if (!hasDescriptor)
            return JsValue.Undefined;

        return FromPropertyDescriptor(realm, descriptor);
    }

    /// <summary>ToPropertyDescriptor(Obj)</summary>
    public static PropertyDescriptor ToPropertyDescriptor(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<PropertyDescriptor>($"Property description must be an object: {JsOperations.DescribeForError(value)}");

        var descriptor = default(PropertyDescriptor);
        if (obj.HasProperty(EnumerableKey))
        {
            descriptor.Fields |= DescriptorFields.Enumerable;
            if (JsOperations.ToBoolean(obj.Get(EnumerableKey)))
            {
                descriptor.Flags |= PropertyFlags.Enumerable;
            }
        }

        if (obj.HasProperty(ConfigurableKey))
        {
            descriptor.Fields |= DescriptorFields.Configurable;
            if (JsOperations.ToBoolean(obj.Get(ConfigurableKey)))
            {
                descriptor.Flags |= PropertyFlags.Configurable;
            }
        }

        if (obj.HasProperty(ValueKey))
        {
            descriptor.Fields |= DescriptorFields.Value;
            descriptor.Value = obj.Get(ValueKey);
        }

        if (obj.HasProperty(WritableKey))
        {
            descriptor.Fields |= DescriptorFields.Writable;
            if (JsOperations.ToBoolean(obj.Get(WritableKey)))
            {
                descriptor.Flags |= PropertyFlags.Writable;
            }
        }

        if (obj.HasProperty(GetKey))
        {
            var getter = obj.Get(GetKey);
            if (!getter.IsUndefined && !getter.IsCallable)
                return ThrowHelper.ThrowTypeError<PropertyDescriptor>($"Getter must be a function: {JsOperations.DescribeForError(getter)}");

            descriptor.Fields |= DescriptorFields.Get;
            descriptor.Get = getter;
        }

        if (obj.HasProperty(SetKey))
        {
            var setter = obj.Get(SetKey);
            if (!setter.IsUndefined && !setter.IsCallable)
                return ThrowHelper.ThrowTypeError<PropertyDescriptor>($"Setter must be a function: {JsOperations.DescribeForError(setter)}");

            descriptor.Fields |= DescriptorFields.Set;
            descriptor.Set = setter;
        }

        if (descriptor.IsAccessorDescriptor && descriptor.IsDataDescriptor)
            return ThrowHelper.ThrowTypeError<PropertyDescriptor>("Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");

        return descriptor;
    }

    /// <summary>CompletePropertyDescriptor(Desc): adds the default values of the absent fields.</summary>
    public static void CompletePropertyDescriptor(ref PropertyDescriptor descriptor)
    {
        if (descriptor.IsGenericDescriptor || descriptor.IsDataDescriptor)
        {
            if (!descriptor.HasValue)
            {
                descriptor.Value = JsValue.Undefined;
            }

            if (!descriptor.HasWritable)
            {
                descriptor.Flags &= ~PropertyFlags.Writable;
            }

            descriptor.Fields |= DescriptorFields.Data;
        }
        else
        {
            if (!descriptor.HasGet)
            {
                descriptor.Get = JsValue.Undefined;
            }

            if (!descriptor.HasSet)
            {
                descriptor.Set = JsValue.Undefined;
            }

            descriptor.Fields |= DescriptorFields.Accessor;
        }

        if (!descriptor.HasEnumerable)
        {
            descriptor.Flags &= ~PropertyFlags.Enumerable;
        }

        if (!descriptor.HasConfigurable)
        {
            descriptor.Flags &= ~PropertyFlags.Configurable;
        }

        descriptor.Fields |= DescriptorFields.Enumerable | DescriptorFields.Configurable;
    }

    /// <summary>IsCompatiblePropertyDescriptor(Extensible, Desc, Current)</summary>
    public static bool IsCompatiblePropertyDescriptor(bool extensible, in PropertyDescriptor descriptor, bool hasCurrent, in PropertyDescriptor current)
    {
        return JsObject.ValidateAndApplyPropertyDescriptor(obj: null, default, extensible, descriptor, hasCurrent, current);
    }

    /// <summary>SetIntegrityLevel(O, level)</summary>
    public static bool SetIntegrityLevel(JsObject obj, IntegrityLevel level)
    {
        if (!obj.PreventExtensions())
            return false;

        var keys = obj.OwnPropertyKeys();
        if (level == IntegrityLevel.Sealed)
        {
            var descriptor = new PropertyDescriptor { Fields = DescriptorFields.Configurable };
            foreach (var key in keys)
            {
                obj.DefinePropertyOrThrow(key, descriptor);
            }
        }
        else
        {
            var accessorDescriptor = new PropertyDescriptor { Fields = DescriptorFields.Configurable };
            var dataDescriptor = new PropertyDescriptor { Fields = DescriptorFields.Configurable | DescriptorFields.Writable };
            foreach (var key in keys)
            {
                if (obj.GetOwnProperty(key, out var current))
                {
                    obj.DefinePropertyOrThrow(key, current.IsAccessorDescriptor ? accessorDescriptor : dataDescriptor);
                }
            }
        }

        return true;
    }

    /// <summary>TestIntegrityLevel(O, level)</summary>
    public static bool TestIntegrityLevel(JsObject obj, IntegrityLevel level)
    {
        if (obj.IsExtensible())
            return false;

        foreach (var key in obj.OwnPropertyKeys())
        {
            if (obj.GetOwnProperty(key, out var current))
            {
                if (current.Configurable)
                    return false;

                if (level == IntegrityLevel.Frozen && current.IsDataDescriptor && current.Writable)
                    return false;
            }
        }

        return true;
    }

    /// <summary>EnumerableOwnProperties(O, kind): the keys, values or [key, value] entries of the enumerable own string-keyed properties.</summary>
    public static List<JsValue> EnumerableOwnProperties(JsObject obj, EnumerableOwnPropertiesKind kind)
    {
        var ownKeys = obj.OwnPropertyKeys();
        var properties = new List<JsValue>(ownKeys.Count);
        foreach (var key in ownKeys)
        {
            if (key.IsSymbol)
                continue;

            if (!obj.GetOwnProperty(key, out var descriptor) || !descriptor.Enumerable)
                continue;

            if (kind == EnumerableOwnPropertiesKind.Key)
            {
                properties.Add(key.ToJsValue());
                continue;
            }

            var value = obj.Get(key);
            if (kind == EnumerableOwnPropertiesKind.Value)
            {
                properties.Add(value);
            }
            else
            {
                var realm = JsEngine.Current!.CurrentRealm!;
                properties.Add(JsArray.Create(realm, [key.ToJsValue(), value]));
            }
        }

        return properties;
    }

    /// <summary>CreateListFromArrayLike(obj): the elements of an array-like object.</summary>
    public static JsValue[] CreateListFromArrayLike(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue[]>("CreateListFromArrayLike called on non-object");

        var length = GetListLength(obj);
        if (length == 0)
            return [];

        var list = new JsValue[length];
        for (var i = 0; i < list.Length; i++)
        {
            list[i] = obj.Get(new PropertyKey((uint)i));
        }

        return list;
    }

    /// <summary>CreateListFromArrayLike(obj, property-key): the elements of an array-like object, which must be strings or symbols.</summary>
    public static List<PropertyKey> CreateListOfPropertyKeysFromArrayLike(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<List<PropertyKey>>("CreateListFromArrayLike called on non-object");

        var length = GetListLength(obj);
        var list = new List<PropertyKey>(length);
        for (var i = 0; i < length; i++)
        {
            var element = obj.Get(new PropertyKey((uint)i));
            switch (element.Cell)
            {
                case JsString str:
                    list.Add(new PropertyKey(str.ToString()));
                    break;
                case JsSymbol symbol:
                    list.Add(new PropertyKey(symbol));
                    break;
                default:
                    return ThrowHelper.ThrowTypeError<List<PropertyKey>>($"{JsOperations.DescribeForError(element)} is not a valid property name");
            }
        }

        return list;
    }

    private static int GetListLength(JsObject obj)
    {
        var length = JsOperations.LengthOfArrayLike(obj);
        if (length > MaxListLength)
        {
            ThrowHelper.ThrowRangeError("Too many arguments in function call");
        }

        return (int)length;
    }

    /// <summary>CopyDataProperties(target, source, excludedItems): copies the enumerable own properties (object spread and rest).</summary>
    public static void CopyDataProperties(JsObject target, JsValue source, ReadOnlySpan<PropertyKey> excludedKeys = default)
    {
        if (source.IsNullOrUndefined)
            return;

        var from = JsOperations.ToObject(source);
        foreach (var key in from.OwnPropertyKeys())
        {
            if (excludedKeys.Contains(key))
                continue;

            if (from.GetOwnProperty(key, out var descriptor) && descriptor.Enumerable)
            {
                var value = from.Get(key);
                target.CreateDataPropertyOrThrow(key, value);
            }
        }
    }

    /// <summary>
    /// AddEntriesFromIterable(target, iterable, adder): calls the adder with the key (element 0) and the value (element 1)
    /// of each entry of the iterable, and closes the iterator when an operation throws.
    /// </summary>
    public static void AddEntriesFromIterable(JsValue iterable, Action<JsValue, JsValue> adder)
    {
        var iteratorRecord = IteratorOperations.GetIterator(iterable);
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            try
            {
                if (next.Cell is not JsObject entry)
                {
                    ThrowHelper.ThrowTypeError($"Iterator value {JsOperations.DescribeForError(next)} is not an entry object");
                    return;
                }

                var key = entry.Get(new PropertyKey(0u));
                var value = entry.Get(new PropertyKey(1u));
                adder(key, value);
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw;
            }
        }
    }

    /// <summary>
    /// GroupBy(items, callback, keyCoercion): groups the values of an iterable by the result of the callback. With
    /// <paramref name="propertyKeys"/>, the keys are converted with ToPropertyKey (Object.groupBy); otherwise -0 is
    /// normalized to +0 (Map.groupBy). The groups are in the order of their first element.
    /// </summary>
    public static List<KeyValuePair<JsValue, List<JsValue>>> GroupBy(JsValue items, JsValue callback, bool propertyKeys)
    {
        JsOperations.RequireObjectCoercible(items);
        if (!callback.IsCallable)
            return ThrowHelper.ThrowTypeError<List<KeyValuePair<JsValue, List<JsValue>>>>($"{JsOperations.DescribeForError(callback)} is not a function");

        var callbackFunction = callback.AsObject();
        var groups = new List<KeyValuePair<JsValue, List<JsValue>>>();
        var propertyKeyIndices = propertyKeys ? new Dictionary<PropertyKey, int>() : null;
        var valueIndices = propertyKeys ? null : new Dictionary<JsValue, int>();
        var iteratorRecord = IteratorOperations.GetIterator(items);
        long k = 0;
        while (true)
        {
            if (k >= 9007199254740991)
            {
                var error = ThrowHelper.CreateException(ErrorType.TypeError, "Too many elements");
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw error;
            }

            if (!IteratorOperations.IteratorStepValue(iteratorRecord, out var value))
                return groups;

            int index;
            try
            {
                var key = callbackFunction.Call(JsValue.Undefined, [value, k]);
                if (propertyKeyIndices is not null)
                {
                    var propertyKey = JsOperations.ToPropertyKey(key);
                    if (!propertyKeyIndices.TryGetValue(propertyKey, out index))
                    {
                        index = groups.Count;
                        propertyKeyIndices.Add(propertyKey, index);
                        groups.Add(new(propertyKey.ToJsValue(), []));
                    }
                }
                else
                {
                    if (key.IsNumber && key.NumberValue == 0)
                    {
                        key = 0;
                    }

                    if (!valueIndices!.TryGetValue(key, out index))
                    {
                        index = groups.Count;
                        valueIndices.Add(key, index);
                        groups.Add(new(key, []));
                    }
                }
            }
            catch (JsException)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw;
            }

            groups[index].Value.Add(value);
            k++;
        }
    }
}
