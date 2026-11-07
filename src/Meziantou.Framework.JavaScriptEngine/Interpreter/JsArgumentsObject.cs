namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// A mapped arguments exotic object (https://tc39.es/ecma262/#sec-arguments-exotic-objects): the indices of the
/// parameters are aliases of the parameter bindings (the slots of the environment of the function) until they are
/// deleted or redefined.
/// </summary>
internal sealed class JsArgumentsObject : JsObject
{
    private readonly DeclarativeEnvironment _environment;

    // The slot of the parameter mapped by each index, or -1 when the index is not mapped
    private readonly int[] _map;

    public JsArgumentsObject(JsObject prototype, DeclarativeEnvironment environment, int[] map)
        : base(prototype, ObjectClass.Arguments)
    {
        _environment = environment;
        _map = map;
        Flags |= ObjectFlags.Exotic;
    }

    private bool IsMapped(in PropertyKey key, out int slot)
    {
        if (key.IsArrayIndex && key.ArrayIndex < (uint)_map.Length && _map[key.ArrayIndex] >= 0)
        {
            slot = _map[key.ArrayIndex];
            return true;
        }

        slot = -1;
        return false;
    }

    private void Unmap(in PropertyKey key) => _map[key.ArrayIndex] = -1;

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (!OrdinaryGetOwnProperty(key, out descriptor))
            return false;

        if (IsMapped(key, out var slot))
        {
            descriptor.Value = _environment.Slots[slot];
        }

        return true;
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        var isMapped = IsMapped(key, out var slot);
        var newDescriptor = descriptor;
        if (isMapped && descriptor.IsDataDescriptor && !descriptor.HasValue && descriptor.HasWritable && !descriptor.Writable)
        {
            newDescriptor.Value = _environment.Slots[slot];
            newDescriptor.Fields |= DescriptorFields.Value;
        }

        if (!OrdinaryDefineOwnProperty(key, newDescriptor))
            return false;

        if (isMapped)
        {
            if (descriptor.IsAccessorDescriptor)
            {
                Unmap(key);
            }
            else
            {
                if (descriptor.HasValue)
                {
                    _environment.Slots[slot] = descriptor.Value;
                }

                if (descriptor.HasWritable && !descriptor.Writable)
                {
                    Unmap(key);
                }
            }
        }

        return true;
    }

    internal override JsValue Get(in PropertyKey key, JsValue receiver)
    {
        if (IsMapped(key, out var slot))
            return _environment.Slots[slot];

        return OrdinaryGet(key, receiver);
    }

    internal override bool Set(in PropertyKey key, JsValue value, JsValue receiver)
    {
        var isMapped = receiver.Cell == this && IsMapped(key, out _);
        if (isMapped)
        {
            IsMapped(key, out var slot);
            _environment.Slots[slot] = value;
        }

        return OrdinarySet(key, value, receiver);
    }

    internal override bool Delete(in PropertyKey key)
    {
        var isMapped = IsMapped(key, out _);
        var result = OrdinaryDelete(key);
        if (result && isMapped)
        {
            Unmap(key);
        }

        return result;
    }
}
