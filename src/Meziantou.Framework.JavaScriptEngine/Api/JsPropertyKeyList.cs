using System.Collections;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The own property keys of an object, returned to the host without copying the list built by the engine.</summary>
internal sealed class JsPropertyKeyList(List<PropertyKey> keys) : IReadOnlyList<JsPropertyKey>
{
    internal List<PropertyKey> Keys => keys;

    public int Count => keys.Count;

    public JsPropertyKey this[int index] => new(keys[index]);

    public IEnumerator<JsPropertyKey> GetEnumerator()
    {
        foreach (var key in keys)
        {
            yield return new JsPropertyKey(key);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
