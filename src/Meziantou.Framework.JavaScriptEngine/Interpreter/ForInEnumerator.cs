namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// EnumerateObjectProperties (https://tc39.es/ecma262/#sec-enumerate-object-properties): the enumerable string keys of
/// the object and of its prototypes, each name once; a property deleted before it is visited is not visited.
/// </summary>
/// <remarks>
/// The keys of an ordinary object whose properties are all in its shape are the keys of the shape (immutable: no list is
/// built). The prototypes that have no enumerable string key, at the end of the chain, are skipped (typically
/// %Object.prototype%), and the set of the visited names is only created when a prototype has enumerable keys.
/// </remarks>
internal sealed class ForInEnumerator(JsObject target) : JsCell(JsValueKind.Undefined)
{
    // The names of the objects already processed (all their string keys: a non-enumerable property hides the properties
    // of the prototypes), created when a second object has keys to visit
    private HashSet<PropertyKey>? _visited;
    private JsObject? _current = target;

    // The keys of the current object: its shape, or a list
    private Shape? _shape;
    private List<PropertyKey>? _keys;
    private int _index;

    public bool MoveNext(out JsValue key)
    {
        while (_current is not null)
        {
            if (_shape is null && _keys is null)
            {
                _shape = _current.GetOrdinaryShapeForEnumeration();
                if (_shape is null)
                {
                    _keys = _current.OwnPropertyKeys();
                }
            }

            if (_shape is { } shape)
            {
                while (_index < shape.Count)
                {
                    var slot = _index++;
                    var candidate = shape.GetKey(slot);
                    if (candidate.IsSymbol || (_visited is not null && !_visited.Add(candidate)))
                        continue;

                    // The property must still exist and be enumerable when it is visited (the shape tells while it is the shape of the object)
                    var enumerable = ReferenceEquals(_current.Map, shape)
                        ? (shape.GetFlags(slot) & PropertyFlags.Enumerable) != 0
                        : _current.GetOwnProperty(candidate, out var descriptor) && descriptor.Enumerable;
                    if (enumerable)
                    {
                        key = new JsValue(shape.GetKeyString(slot));
                        return true;
                    }
                }
            }
            else
            {
                var keys = _keys!;
                while (_index < keys.Count)
                {
                    var candidate = keys[_index++];
                    if (candidate.IsSymbol || (_visited is not null && !_visited.Add(candidate)))
                        continue;

                    if (_current.GetOwnProperty(candidate, out var descriptor) && descriptor.Enumerable)
                    {
                        key = JsValue.FromString(candidate.AsString());
                        return true;
                    }
                }
            }

            NextObject();
        }

        key = default;
        return false;
    }

    /// <summary>Moves to the prototype of the current object (the end when no prototype has an enumerable key).</summary>
    private void NextObject()
    {
        var previous = _current!;
        var previousShape = _shape;
        var previousKeys = _keys;
        _current = previous.GetPrototypeOf();
        _shape = null;
        _keys = null;
        _index = 0;
        if (_current is null || !HasEnumerableKeysInChain(_current))
        {
            _current = null;
            return;
        }

        // A prototype has keys to visit: the names of the first object hide its properties (the names of the next objects
        // are added to the set while they are visited)
        if (_visited is null)
        {
            _visited = [];
            AddStringKeys(_visited, previousShape, previousKeys);
        }

        static void AddStringKeys(HashSet<PropertyKey> visited, Shape? shape, List<PropertyKey>? keys)
        {
            if (shape is not null)
            {
                for (var i = 0; i < shape.Count; i++)
                {
                    var key = shape.GetKey(i);
                    if (key.IsString)
                    {
                        visited.Add(key);
                    }
                }
            }
            else if (keys is not null)
            {
                foreach (var key in keys)
                {
                    if (key.IsString)
                    {
                        visited.Add(key);
                    }
                }
            }
        }
    }

    /// <summary>Gets whether an object or one of its prototypes may have an enumerable string key (true when it cannot tell: exotic objects).</summary>
    private static bool HasEnumerableKeysInChain(JsObject obj)
    {
        for (var current = obj; current is not null; current = current.PrototypeSlot)
        {
            if (current.IsExotic || current.MayHaveEnumerableStringKeys())
                return true;
        }

        return false;
    }
}
