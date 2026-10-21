namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A String exotic object (https://tc39.es/ecma262/#sec-string-exotic-objects): [[StringData]], with the indices and the length as properties.</summary>
internal sealed class JsStringObject : JsObject
{
    public JsStringObject(JsObject? prototype, JsString value)
        : base(prototype, ObjectClass.String)
    {
        StringData = value;
        Flags |= ObjectFlags.Exotic;
        DefineBuiltIn("length", value.Length, PropertyFlags.None);
    }

    public JsString StringData { get; }

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (OrdinaryGetOwnProperty(key, out descriptor))
            return true;

        return StringGetOwnProperty(key, out descriptor);
    }

    private bool StringGetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (key.IsArrayIndex && key.ArrayIndex < (uint)StringData.Length)
        {
            descriptor = PropertyDescriptor.Data(JsValue.FromJsString(JsString.Create(StringData[(int)key.ArrayIndex])), PropertyFlags.Enumerable);
            return true;
        }

        descriptor = default;
        return false;
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        if (StringGetOwnProperty(key, out var current))
            return ValidateAndApplyPropertyDescriptor(obj: null, key, IsExtensible(), descriptor, hasCurrent: true, current);

        return OrdinaryDefineOwnProperty(key, descriptor);
    }

    internal override List<PropertyKey> OwnPropertyKeys()
    {
        var keys = new List<PropertyKey>();
        for (var i = 0; i < StringData.Length; i++)
        {
            keys.Add(new PropertyKey((uint)i));
        }

        foreach (var key in OrdinaryOwnPropertyKeys())
        {
            if (key.IsArrayIndex && key.ArrayIndex < (uint)StringData.Length)
                continue;

            keys.Add(key);
        }

        // The integer indices of the ordinary properties are sorted after the indices of the string
        var indexCount = keys.FindLastIndex(k => k.IsArrayIndex) + 1;
        keys.Sort(0, indexCount, Comparer<PropertyKey>.Create(static (a, b) => a.ArrayIndex.CompareTo(b.ArrayIndex)));
        return keys;
    }
}
