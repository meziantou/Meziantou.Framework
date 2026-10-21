namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A Proxy exotic object (https://tc39.es/ecma262/#sec-proxy-object-internal-methods-and-internal-slots): its internal
/// methods call the traps of its handler, and check the invariants of the internal methods against its target.
/// </summary>
public sealed class JsProxy : JsObject, IProxyTarget
{
    private static readonly PropertyKey GetPrototypeOfKey = new("getPrototypeOf");
    private static readonly PropertyKey SetPrototypeOfKey = new("setPrototypeOf");
    private static readonly PropertyKey IsExtensibleKey = new("isExtensible");
    private static readonly PropertyKey PreventExtensionsKey = new("preventExtensions");
    private static readonly PropertyKey GetOwnPropertyDescriptorKey = new("getOwnPropertyDescriptor");
    private static readonly PropertyKey DefinePropertyKey = new("defineProperty");
    private static readonly PropertyKey HasKey = new("has");
    private static readonly PropertyKey GetKey = new("get");
    private static readonly PropertyKey SetKey = new("set");
    private static readonly PropertyKey DeletePropertyKey = new("deleteProperty");
    private static readonly PropertyKey OwnKeysKey = new("ownKeys");
    private static readonly PropertyKey ApplyKey = new("apply");
    private static readonly PropertyKey ConstructKey = new("construct");

    private readonly JsEngine _engine;
    private JsObject? _target;
    private JsObject? _handler;

    private JsProxy(JsEngine engine, JsObject target, JsObject handler)
        : base(prototype: null, ObjectClass.Proxy)
    {
        _engine = engine;
        _target = target;
        _handler = handler;
        Flags |= ObjectFlags.Exotic;
        if (target.IsCallable)
        {
            Flags |= ObjectFlags.Callable;
            if (target.IsConstructor)
            {
                Flags |= ObjectFlags.Constructor;
            }
        }
    }

    /// <summary>Gets the target of the proxy ([[ProxyTarget]]), or null when the proxy is revoked.</summary>
    public JsObject? Target => _target;

    /// <summary>Gets the handler of the proxy ([[ProxyHandler]]), or null when the proxy is revoked.</summary>
    public JsObject? Handler => _handler;

    /// <summary>Gets whether the proxy is revoked.</summary>
    public bool IsRevoked => _handler is null;

    /// <summary>ProxyCreate(target, handler)</summary>
    internal static JsProxy Create(JsEngine engine, JsValue target, JsValue handler)
    {
        if (target.Cell is not JsObject targetObject)
            return ThrowHelper.ThrowTypeError<JsProxy>("Cannot create proxy with a non-object as target or handler");

        if (handler.Cell is not JsObject handlerObject)
            return ThrowHelper.ThrowTypeError<JsProxy>("Cannot create proxy with a non-object as target or handler");

        return new JsProxy(engine, targetObject, handlerObject);
    }

    /// <summary>Revokes the proxy: its internal methods throw a TypeError.</summary>
    internal void Revoke()
    {
        _target = null;
        _handler = null;
    }

    /// <inheritdoc />
    JsObject IProxyTarget.GetProxyTargetOrThrow()
    {
        if (_target is null)
            return ThrowHelper.ThrowTypeError<JsObject>("Cannot perform the operation on a proxy that has been revoked");

        return _target;
    }

    private static T ThrowRevoked<T>(string operation) => ThrowHelper.ThrowTypeError<T>($"Cannot perform '{operation}' on a proxy that has been revoked");

    /// <summary>ValidateNonRevokedProxy, then GetMethod(handler, name): the trap, or null to forward to the target.</summary>
    private JsObject? GetTrap(in PropertyKey name, out JsObject target, out JsObject handler)
    {
        if (_handler is null)
        {
            target = null!;
            handler = null!;
            return ThrowRevoked<JsObject?>(name.ToString());
        }

        handler = _handler;
        target = _target!;
        return JsOperations.GetMethod(handler, name);
    }

    private static JsRealm CurrentRealm => JsEngine.Current!.CurrentRealm!;

    internal override JsObject? GetPrototypeOf()
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(GetPrototypeOfKey, out var target, out var handler);
            if (trap is null)
                return target.GetPrototypeOf();

            var handlerProto = trap.Call(handler, [target]);
            if (!handlerProto.IsObject && !handlerProto.IsNull)
                return ThrowHelper.ThrowTypeError<JsObject?>("'getPrototypeOf' on proxy: trap returned neither object nor null");

            var result = handlerProto.TryGetObject();
            if (target.IsExtensible())
                return result;

            var targetProto = target.GetPrototypeOf();
            if (!ReferenceEquals(result, targetProto))
                return ThrowHelper.ThrowTypeError<JsObject?>("'getPrototypeOf' on proxy: proxy target is non-extensible but the trap did not return its actual prototype");

            return result;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool SetPrototypeOf(JsObject? prototype)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(SetPrototypeOfKey, out var target, out var handler);
            if (trap is null)
                return target.SetPrototypeOf(prototype);

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target, JsValue.FromObject(prototype)]));
            if (!booleanTrapResult)
                return false;

            if (target.IsExtensible())
                return true;

            var targetProto = target.GetPrototypeOf();
            if (!ReferenceEquals(prototype, targetProto))
                return ThrowHelper.ThrowTypeError<bool>("'setPrototypeOf' on proxy: trap returned truish for setting a new prototype on the non-extensible proxy target");

            return true;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool IsExtensible()
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(IsExtensibleKey, out var target, out var handler);
            if (trap is null)
                return target.IsExtensible();

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target]));
            var targetResult = target.IsExtensible();
            if (booleanTrapResult != targetResult)
                return ThrowHelper.ThrowTypeError<bool>($"'isExtensible' on proxy: trap result does not reflect extensibility of proxy target (which is '{(targetResult ? "true" : "false")}')");

            return booleanTrapResult;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool PreventExtensions()
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(PreventExtensionsKey, out var target, out var handler);
            if (trap is null)
                return target.PreventExtensions();

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target]));
            if (booleanTrapResult && target.IsExtensible())
                return ThrowHelper.ThrowTypeError<bool>("'preventExtensions' on proxy: trap returned truish but the proxy target is extensible");

            return booleanTrapResult;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(GetOwnPropertyDescriptorKey, out var target, out var handler);
            if (trap is null)
                return target.GetOwnProperty(key, out descriptor);

            var trapResultObj = trap.Call(handler, [target, key.ToJsValue()]);
            if (!trapResultObj.IsObject && !trapResultObj.IsUndefined)
            {
                descriptor = default;
                return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap returned neither object nor undefined for property '{key}'");
            }

            var hasTargetDesc = target.GetOwnProperty(key, out var targetDesc);
            if (trapResultObj.IsUndefined)
            {
                descriptor = default;
                if (!hasTargetDesc)
                    return false;

                if (!targetDesc.Configurable)
                    return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap returned undefined for property '{key}' which is non-configurable in the proxy target");

                if (!target.IsExtensible())
                    return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap returned undefined for property '{key}' which exists in the non-extensible proxy target");

                return false;
            }

            var extensibleTarget = target.IsExtensible();
            var resultDesc = PropertyOperations.ToPropertyDescriptor(trapResultObj);
            PropertyOperations.CompletePropertyDescriptor(ref resultDesc);
            if (!PropertyOperations.IsCompatiblePropertyDescriptor(extensibleTarget, resultDesc, hasTargetDesc, targetDesc))
            {
                descriptor = default;
                return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap returned descriptor for property '{key}' that is incompatible with the existing property in the proxy target");
            }

            if (!resultDesc.Configurable)
            {
                if (!hasTargetDesc || targetDesc.Configurable)
                {
                    descriptor = default;
                    return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap reported non-configurability for property '{key}' which is either non-existent or configurable in the proxy target");
                }

                if (resultDesc.HasWritable && !resultDesc.Writable && targetDesc.Writable)
                {
                    descriptor = default;
                    return ThrowHelper.ThrowTypeError<bool>($"'getOwnPropertyDescriptor' on proxy: trap reported non-configurable and writable for property '{key}' which is non-configurable, non-writable in the proxy target");
                }
            }

            descriptor = resultDesc;
            return true;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(DefinePropertyKey, out var target, out var handler);
            if (trap is null)
                return target.DefineOwnProperty(key, descriptor);

            var descObj = PropertyOperations.FromPropertyDescriptor(CurrentRealm, descriptor);
            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target, key.ToJsValue(), descObj]));
            if (!booleanTrapResult)
                return false;

            var hasTargetDesc = target.GetOwnProperty(key, out var targetDesc);
            var extensibleTarget = target.IsExtensible();
            var settingConfigFalse = descriptor.HasConfigurable && !descriptor.Configurable;
            if (!hasTargetDesc)
            {
                if (!extensibleTarget)
                    return ThrowHelper.ThrowTypeError<bool>($"'defineProperty' on proxy: trap returned truish for adding property '{key}' to the non-extensible proxy target");

                if (settingConfigFalse)
                    return ThrowHelper.ThrowTypeError<bool>($"'defineProperty' on proxy: trap returned truish for defining non-configurable property '{key}' which is non-existent in the proxy target");
            }
            else
            {
                if (!PropertyOperations.IsCompatiblePropertyDescriptor(extensibleTarget, descriptor, hasCurrent: true, targetDesc))
                    return ThrowHelper.ThrowTypeError<bool>($"'defineProperty' on proxy: trap returned truish for adding property '{key}' that is incompatible with the existing property in the proxy target");

                if (settingConfigFalse && targetDesc.Configurable)
                    return ThrowHelper.ThrowTypeError<bool>($"'defineProperty' on proxy: trap returned truish for defining non-configurable property '{key}' which is configurable in the proxy target");

                if (targetDesc.IsDataDescriptor && !targetDesc.Configurable && targetDesc.Writable && descriptor.HasWritable && !descriptor.Writable)
                    return ThrowHelper.ThrowTypeError<bool>($"'defineProperty' on proxy: trap returned truish for defining non-configurable property '{key}' which cannot be non-writable, unless there exists a corresponding non-configurable, non-writable own property of the target object");
            }

            return true;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool HasProperty(in PropertyKey key)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(HasKey, out var target, out var handler);
            if (trap is null)
                return target.HasProperty(key);

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target, key.ToJsValue()]));
            if (!booleanTrapResult && target.GetOwnProperty(key, out var targetDesc))
            {
                if (!targetDesc.Configurable)
                    return ThrowHelper.ThrowTypeError<bool>($"'has' on proxy: trap returned falsish for property '{key}' which exists in the proxy target as non-configurable");

                if (!target.IsExtensible())
                    return ThrowHelper.ThrowTypeError<bool>($"'has' on proxy: trap returned falsish for property '{key}' but the proxy target is not extensible");
            }

            return booleanTrapResult;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override JsValue Get(in PropertyKey key, JsValue receiver)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(GetKey, out var target, out var handler);
            if (trap is null)
                return target.Get(key, receiver);

            var trapResult = trap.Call(handler, [target, key.ToJsValue(), receiver]);
            if (target.GetOwnProperty(key, out var targetDesc) && !targetDesc.Configurable)
            {
                if (targetDesc.IsDataDescriptor && !targetDesc.Writable && !JsOperations.SameValue(trapResult, targetDesc.Value))
                    return ThrowHelper.ThrowTypeError<JsValue>($"'get' on proxy: property '{key}' is a read-only and non-configurable data property on the proxy target but the proxy did not return its actual value");

                if (targetDesc.IsAccessorDescriptor && targetDesc.Get.IsUndefined && !trapResult.IsUndefined)
                    return ThrowHelper.ThrowTypeError<JsValue>($"'get' on proxy: property '{key}' is a non-configurable accessor property on the proxy target and does not have a getter function, but the trap did not return 'undefined'");
            }

            return trapResult;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool Set(in PropertyKey key, JsValue value, JsValue receiver)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(SetKey, out var target, out var handler);
            if (trap is null)
                return target.Set(key, value, receiver);

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target, key.ToJsValue(), value, receiver]));
            if (!booleanTrapResult)
                return false;

            if (target.GetOwnProperty(key, out var targetDesc) && !targetDesc.Configurable)
            {
                if (targetDesc.IsDataDescriptor && !targetDesc.Writable && !JsOperations.SameValue(value, targetDesc.Value))
                    return ThrowHelper.ThrowTypeError<bool>($"'set' on proxy: trap returned truish for property '{key}' which exists in the proxy target as a non-configurable and non-writable data property with a different value");

                if (targetDesc.IsAccessorDescriptor && targetDesc.Set.IsUndefined)
                    return ThrowHelper.ThrowTypeError<bool>($"'set' on proxy: trap returned truish for property '{key}' which exists in the proxy target as a non-configurable and non-writable accessor property without a setter");
            }

            return true;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override bool Delete(in PropertyKey key)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(DeletePropertyKey, out var target, out var handler);
            if (trap is null)
                return target.Delete(key);

            var booleanTrapResult = JsOperations.ToBoolean(trap.Call(handler, [target, key.ToJsValue()]));
            if (!booleanTrapResult)
                return false;

            if (!target.GetOwnProperty(key, out var targetDesc))
                return true;

            if (!targetDesc.Configurable)
                return ThrowHelper.ThrowTypeError<bool>($"'deleteProperty' on proxy: trap returned truish for property '{key}' which is non-configurable in the proxy target");

            if (!target.IsExtensible())
                return ThrowHelper.ThrowTypeError<bool>($"'deleteProperty' on proxy: trap returned truish for property '{key}' but the proxy target is non-extensible");

            return true;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override List<PropertyKey> OwnPropertyKeys()
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(OwnKeysKey, out var target, out var handler);
            if (trap is null)
                return target.OwnPropertyKeys();

            var trapResultArray = trap.Call(handler, [target]);
            var trapResult = PropertyOperations.CreateListOfPropertyKeysFromArrayLike(trapResultArray);
            var uncheckedResultKeys = new HashSet<PropertyKey>(trapResult.Count);
            foreach (var key in trapResult)
            {
                if (!uncheckedResultKeys.Add(key))
                    return ThrowHelper.ThrowTypeError<List<PropertyKey>>($"'ownKeys' on proxy: trap returned duplicate entries ('{key}')");
            }

            var extensibleTarget = target.IsExtensible();
            var targetKeys = target.OwnPropertyKeys();
            List<PropertyKey>? targetConfigurableKeys = null;
            List<PropertyKey>? targetNonconfigurableKeys = null;
            foreach (var key in targetKeys)
            {
                if (target.GetOwnProperty(key, out var desc) && !desc.Configurable)
                {
                    (targetNonconfigurableKeys ??= []).Add(key);
                }
                else
                {
                    (targetConfigurableKeys ??= []).Add(key);
                }
            }

            if (extensibleTarget && targetNonconfigurableKeys is null)
                return trapResult;

            if (targetNonconfigurableKeys is not null)
            {
                foreach (var key in targetNonconfigurableKeys)
                {
                    if (!uncheckedResultKeys.Remove(key))
                        return ThrowHelper.ThrowTypeError<List<PropertyKey>>($"'ownKeys' on proxy: trap result did not include '{key}'");
                }
            }

            if (extensibleTarget)
                return trapResult;

            if (targetConfigurableKeys is not null)
            {
                foreach (var key in targetConfigurableKeys)
                {
                    if (!uncheckedResultKeys.Remove(key))
                        return ThrowHelper.ThrowTypeError<List<PropertyKey>>($"'ownKeys' on proxy: trap result did not include '{key}'");
                }
            }

            if (uncheckedResultKeys.Count != 0)
                return ThrowHelper.ThrowTypeError<List<PropertyKey>>("'ownKeys' on proxy: trap returned extra keys but proxy target is non-extensible");

            return trapResult;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(ApplyKey, out var target, out var handler);
            if (trap is null)
                return target.Call(thisValue, arguments);

            var argArray = JsArray.Create(CurrentRealm, arguments);
            return trap.Call(handler, [target, thisValue, argArray]);
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override JsValue Construct(ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        _engine.EnterRecursion();
        try
        {
            var trap = GetTrap(ConstructKey, out var target, out var handler);
            if (trap is null)
                return target.Construct(arguments, newTarget);

            var argArray = JsArray.Create(CurrentRealm, arguments);
            var newObj = trap.Call(handler, [target, argArray, newTarget]);
            if (!newObj.IsObject)
                return ThrowHelper.ThrowTypeError<JsValue>("'construct' on proxy: trap returned non-object");

            return newObj;
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }
}
