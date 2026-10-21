namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// EnumerateObjectProperties (https://tc39.es/ecma262/#sec-enumerate-object-properties): the enumerable string keys of
/// the object and of its prototypes, each name once; a property deleted before it is visited is not visited.
/// </summary>
internal sealed class ForInEnumerator(JsObject target) : JsCell(JsValueKind.Undefined)
{
    private readonly HashSet<PropertyKey> _visited = [];
    private JsObject? _current = target;
    private List<PropertyKey>? _keys;
    private int _index;

    public bool MoveNext(out JsValue key)
    {
        while (_current is not null)
        {
            _keys ??= _current.OwnPropertyKeys();
            while (_index < _keys.Count)
            {
                var candidate = _keys[_index++];
                if (candidate.IsSymbol)
                    continue;

                if (!_visited.Add(candidate))
                    continue;

                // The property must still exist and be enumerable when it is visited
                if (_current.GetOwnProperty(candidate, out var descriptor) && descriptor.Enumerable)
                {
                    key = JsValue.FromString(candidate.AsString());
                    return true;
                }
            }

            _current = _current.GetPrototypeOf();
            _keys = null;
            _index = 0;
        }

        key = default;
        return false;
    }
}
