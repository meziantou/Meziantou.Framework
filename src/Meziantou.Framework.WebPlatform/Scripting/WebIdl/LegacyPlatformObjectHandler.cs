using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The internal methods of the legacy platform objects, which support indexed or named properties
/// (https://webidl.spec.whatwg.org/#es-legacy-platform-objects).
/// </summary>
internal sealed class LegacyPlatformObjectHandler<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, InterfaceBinding<TAdapter, TValue> binding, LegacyPlatformObjectBinding<TAdapter, TValue> legacy) : IJsHostObjectHandler<TValue>
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    private TAdapter Adapter => realm.Adapter;

    private static object Target(WebIdlRealm<TAdapter, TValue> realm, TValue obj) => realm.Adapter.TryGetPlatformObject(obj, out var target) ? target : throw new InvalidOperationException("The object is not a platform object");

    private bool TryGetArrayIndex(TValue property, out uint index)
    {
        index = 0;
        if (!Adapter.IsString(property))
            return false;

        var s = Adapter.AsString(property);
        if (s.Length == 0 || s.Length > 10 || (s.Length > 1 && s[0] == '0'))
            return false;

        foreach (var c in s)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return uint.TryParse(s, out index) && index != uint.MaxValue;
    }

    private Html.CEReactionsScope EnterSpecialOperation()
        => (binding.Flags & InterfaceBindingFlags.SpecialOperationsHaveCEReactions) != InterfaceBindingFlags.None ? Html.CEReactionsScope.EnterAndPush() : Html.CEReactionsScope.EnterWithoutPush();

    // The operations are static lambdas with an explicit state: the methods would otherwise allocate a closure on each call,
    // for the properties that are not indexed or named properties too (e.g. the methods of the prototype)
    private T Invoke<TState, T>(TState state, Func<TState, T> action)
    {
        try
        {
            return action(state);
        }
        catch (Exception ex) when (!Adapter.TryGetThrownValue(ex, out _))
        {
            throw realm.ToJsException(ex);
        }
    }

    /// <summary>Returns whether the name is a supported property name of the object (without computing all of them when the object can tell).</summary>
    private bool IsSupportedPropertyName(object target, string name)
    {
        if (target is ISupportedPropertyNames names)
            return names.IsSupportedPropertyName(name);

        return Invoke((legacy, target), static state => state.legacy.SupportedPropertyNames!(state.target)).Contains(name);
    }

    /// <summary>https://webidl.spec.whatwg.org/#dfn-named-property-visibility</summary>
    private bool IsNamedPropertyVisible(TValue obj, object target, string name)
    {
        if (!IsSupportedPropertyName(target, name))
            return false;

        var key = Adapter.FromString(name);
        if (Adapter.OrdinaryGetOwnProperty(obj, key, out _))
            return false;

        if ((binding.Flags & InterfaceBindingFlags.LegacyOverrideBuiltIns) != InterfaceBindingFlags.None)
            return true;

        for (var prototype = Adapter.GetPrototypeOf(obj); !Adapter.IsNull(prototype); prototype = Adapter.GetPrototypeOf(prototype))
        {
            if (Adapter.GetOwnProperty(prototype, key, out _))
                return false;
        }

        return true;
    }

    /// <summary>https://webidl.spec.whatwg.org/#LegacyPlatformObjectGetOwnProperty</summary>
    public bool TryGetOwnProperty(TValue obj, TValue key, out JsPropertyDescriptor<TValue> descriptor)
    {
        var target = Target(realm, obj);
        var ignoreNamedProperties = false;
        if (legacy.SupportsIndexedProperties && TryGetArrayIndex(key, out var index))
        {
            if (index < Invoke((legacy, target), static state => state.legacy.Length!(state.target)))
            {
                var value = Invoke((legacy, realm, target, index), static state => state.legacy.IndexedGetter!(state.realm, state.target, state.index));
                descriptor = JsPropertyDescriptor<TValue>.Data(value, writable: legacy.IndexedSetter is not null, enumerable: true, configurable: true);
                return true;
            }

            ignoreNamedProperties = true;
        }

        if (legacy.SupportsNamedProperties && !ignoreNamedProperties && Adapter.IsString(key) && IsNamedPropertyVisible(obj, target, Adapter.AsString(key)))
        {
            var name = Adapter.AsString(key);
            var value = Invoke((legacy, realm, target, name), static state => state.legacy.NamedGetter!(state.realm, state.target, state.name));
            var enumerable = (binding.Flags & InterfaceBindingFlags.LegacyUnenumerableNamedProperties) == InterfaceBindingFlags.None;
            descriptor = JsPropertyDescriptor<TValue>.Data(value, writable: legacy.NamedSetter is not null, enumerable, configurable: true);
            return true;
        }

        descriptor = default;
        return false;
    }

    /// <summary>https://webidl.spec.whatwg.org/#legacy-platform-object-set</summary>
    public bool TrySet(TValue obj, TValue key, TValue value, out bool result)
    {
        var target = Target(realm, obj);
        if (legacy.SupportsIndexedProperties && TryGetArrayIndex(key, out var index))
        {
            if (legacy.IndexedSetter is null)
            {
                result = false;
                return true;
            }

            using var scope = EnterSpecialOperation();
            Invoke((legacy, realm, target, index, value), static state =>
            {
                state.legacy.IndexedSetter!(state.realm, state.target, state.index, state.value);
                return true;
            });
            result = true;
            return true;
        }

        if (legacy.NamedSetter is not null && Adapter.IsString(key))
        {
            using var scope = EnterSpecialOperation();
            var name = Adapter.AsString(key);
            Invoke((legacy, realm, target, name, value), static state =>
            {
                state.legacy.NamedSetter!(state.realm, state.target, state.name, state.value);
                return true;
            });
            result = true;
            return true;
        }

        // OrdinarySetWithOwnDescriptor(O, P, V, O, LegacyPlatformObjectGetOwnProperty(O, P, ignoreNamedProps: true)): a
        // visible named property does not prevent the assignment of a property of the prototype chain (e.g. form.method
        // when a form control is named "method")
        if (legacy.SupportsNamedProperties && Adapter.IsString(key) && IsNamedPropertyVisible(obj, target, Adapter.AsString(key)))
        {
            var parent = Adapter.GetPrototypeOf(obj);
            if (!Adapter.IsNull(parent))
            {
                result = Adapter.ToBoolean(Adapter.Call(realm.ReflectSet, Adapter.Undefined, [parent, key, value, obj]));
                return true;
            }
        }

        result = false;
        return false;
    }

    public bool TrySet(TValue obj, TValue key, TValue value, TValue receiver, out bool result)
    {
        // OrdinarySetWithOwnDescriptor(O, P, V, Receiver, LegacyPlatformObjectGetOwnProperty(O, P, ignoreNamedProps: true)):
        // a visible named property is ignored, so the assignment creates a property of the receiver (or runs the setter of a
        // property of the prototype chain)
        if (legacy.SupportsNamedProperties && Adapter.IsString(key) && !(legacy.SupportsIndexedProperties && TryGetArrayIndex(key, out _)) && IsNamedPropertyVisible(obj, Target(realm, obj), Adapter.AsString(key)))
        {
            var parent = Adapter.GetPrototypeOf(obj);
            if (!Adapter.IsNull(parent))
            {
                result = Adapter.ToBoolean(Adapter.Call(realm.ReflectSet, Adapter.Undefined, [parent, key, value, receiver]));
                return true;
            }
        }

        result = false;
        return false;
    }

    /// <summary>https://webidl.spec.whatwg.org/#legacy-platform-object-defineownproperty</summary>
    public bool TryDefineOwnProperty(TValue obj, TValue key, in JsPropertyDescriptor<TValue> descriptor, out bool result)
    {
        var target = Target(realm, obj);
        if (legacy.SupportsIndexedProperties && TryGetArrayIndex(key, out var index))
        {
            if (legacy.IndexedSetter is null || !descriptor.IsDataDescriptor)
            {
                result = false;
                return true;
            }

            var value = descriptor.Value ?? Adapter.Undefined;
            using var scope = EnterSpecialOperation();
            Invoke((legacy, realm, target, index, value), static state =>
            {
                state.legacy.IndexedSetter!(state.realm, state.target, state.index, state.value);
                return true;
            });
            result = true;
            return true;
        }

        if (legacy.SupportsNamedProperties && Adapter.IsString(key))
        {
            var name = Adapter.AsString(key);
            var creating = !IsSupportedPropertyName(target, name);
            if ((binding.Flags & InterfaceBindingFlags.LegacyOverrideBuiltIns) != InterfaceBindingFlags.None || !Adapter.OrdinaryGetOwnProperty(obj, key, out _))
            {
                if (!creating && legacy.NamedSetter is null)
                {
                    result = false;
                    return true;
                }

                if (legacy.NamedSetter is not null)
                {
                    if (!descriptor.IsDataDescriptor)
                    {
                        result = false;
                        return true;
                    }

                    var value = descriptor.Value ?? Adapter.Undefined;
                    using var scope = EnterSpecialOperation();
                    Invoke((legacy, realm, target, name, value), static state =>
                    {
                        state.legacy.NamedSetter!(state.realm, state.target, state.name, state.value);
                        return true;
                    });
                    result = true;
                    return true;
                }
            }
        }

        result = false;
        return false;
    }

    /// <summary>https://webidl.spec.whatwg.org/#legacy-platform-object-delete</summary>
    public bool TryDelete(TValue obj, TValue key, out bool result)
    {
        var target = Target(realm, obj);
        if (legacy.SupportsIndexedProperties && TryGetArrayIndex(key, out var index))
        {
            result = index >= Invoke((legacy, target), static state => state.legacy.Length!(state.target));
            return true;
        }

        if (legacy.SupportsNamedProperties && Adapter.IsString(key) && IsNamedPropertyVisible(obj, target, Adapter.AsString(key)))
        {
            if (legacy.NamedDeleter is null)
            {
                result = false;
                return true;
            }

            var name = Adapter.AsString(key);
            using var scope = EnterSpecialOperation();
            Invoke((legacy, realm, target, name), static state =>
            {
                state.legacy.NamedDeleter!(state.realm, state.target, state.name);
                return true;
            });
            result = true;
            return true;
        }

        result = false;
        return false;
    }

    /// <summary>https://webidl.spec.whatwg.org/#legacy-platform-object-ownpropertykeys</summary>
    public IReadOnlyList<TValue>? GetOwnKeys(TValue obj)
    {
        var target = Target(realm, obj);
        var keys = new List<TValue>();
        if (legacy.SupportsIndexedProperties)
        {
            var length = Invoke((legacy, target), static state => state.legacy.Length!(state.target));
            for (uint i = 0; i < length; i++)
            {
                keys.Add(Adapter.FromString(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        if (legacy.SupportsNamedProperties)
        {
            foreach (var name in Invoke((legacy, target), static state => state.legacy.SupportedPropertyNames!(state.target)))
            {
                if (IsNamedPropertyVisible(obj, target, name))
                {
                    keys.Add(Adapter.FromString(name));
                }
            }
        }

        return keys;
    }
}
