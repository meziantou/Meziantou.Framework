namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_ReadOnlyDictionaryObject
/// <summary>
/// Exposes a dictionary to the scripts: its entries are read-only properties, read from the dictionary on each access.
/// </summary>
public sealed class ReadOnlyDictionaryObject(JsRealm realm, IReadOnlyDictionary<string, string> values)
    : JsHostObject(realm.ObjectPrototype, values)
{
    protected override bool GetOwnPropertyCore(JsPropertyKey key, out JsPropertyDescriptor descriptor)
    {
        if (key.IsString && values.TryGetValue(key.AsString(), out var value))
        {
            descriptor = JsPropertyDescriptor.Data(value, writable: false, enumerable: true, configurable: false);
            return true;
        }

        descriptor = default;
        return false;
    }

    // Object.keys, for...in, JSON.stringify...
    protected override IReadOnlyList<JsPropertyKey> OwnPropertyKeysCore() => [.. values.Keys.Select(key => new JsPropertyKey(key))];

    // The scripts cannot add or delete properties
    protected override bool DefineOwnPropertyCore(JsPropertyKey key, in JsPropertyDescriptor descriptor) => false;

    protected override bool DeleteCore(JsPropertyKey key) => !key.IsString || !values.ContainsKey(key.AsString());
}
#endregion
