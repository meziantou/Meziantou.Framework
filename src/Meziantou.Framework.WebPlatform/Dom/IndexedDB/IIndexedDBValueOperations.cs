using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The operations of IndexedDB on the values of a realm (the JavaScript values of a script engine, see
/// <see cref="ScriptEngine.GetIndexedDBValueOperations"/>): the conversions between the values and the keys, the key paths
/// and the serialization of the values for storage. The values are given as the IDL <c>any</c> values (null for
/// <c>undefined</c>).
/// </summary>
internal interface IIndexedDBValueOperations
{
    /// <summary>
    /// https://w3c.github.io/IndexedDB/#convert-value-to-key (or https://w3c.github.io/IndexedDB/#convert-a-value-to-a-multientry-key):
    /// returns null when the value is not a valid key. The exceptions of the code of the value (e.g. a getter) propagate.
    /// </summary>
    IdbKey? ConvertToKey(object? value, bool multiEntry = false);

    /// <summary>https://w3c.github.io/IndexedDB/#convert-a-key-to-a-value: a new value of the realm.</summary>
    object? ConvertToValue(IdbKey key);

    /// <summary>
    /// https://w3c.github.io/IndexedDB/#evaluate-a-key-path-on-a-value for a string key path: returns false (failure) when an
    /// identifier is not an own property of the value.
    /// </summary>
    bool TryEvaluateKeyPath(object? value, string keyPath, out object? result);

    /// <summary>https://w3c.github.io/IndexedDB/#check-that-a-key-could-be-injected-into-a-value</summary>
    bool CanInjectKey(object? value, string keyPath);

    /// <summary>https://w3c.github.io/IndexedDB/#inject-a-key-into-a-value-using-a-key-path</summary>
    void InjectKey(object? value, IdbKey key, string keyPath);

    /// <summary>
    /// StructuredSerializeForStorage (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializeforstorage):
    /// the engine-independent records of the value. Throws a <c>DataCloneError</c> when the value cannot be serialized.
    /// </summary>
    AgentSerializedValue SerializeForStorage(object? value);

    /// <summary>StructuredDeserialize (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize) in the realm.</summary>
    object? Deserialize(AgentSerializedValue serialized);

    /// <summary>Whether the value is <c>undefined</c> (null) or <c>null</c>.</summary>
    bool IsNullOrUndefined(object? value);

    /// <summary>
    /// Whether converting the value to a key does not return "invalid type" (a number, a string, a date, a buffer source
    /// or an array), for https://w3c.github.io/IndexedDB/#is-a-potentially-valid-key-range.
    /// </summary>
    bool IsPotentiallyValidKey(object? value);

    /// <summary>Creates an array of the realm with the given values (e.g. the result of <c>getAll()</c>).</summary>
    object CreateArray(IReadOnlyList<object?> items);

    /// <summary>Converts a value to an <see cref="IdbGetAllOptions"/> dictionary (https://webidl.spec.whatwg.org/#js-dictionary).</summary>
    IdbGetAllOptions ConvertGetAllOptions(object? value);
}
