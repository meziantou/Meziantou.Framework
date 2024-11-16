using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework;

public sealed class QueryStringParameterCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    // The number of parameters is often small, so we use a List instead of a Dictionary.
    // Also, we want to preserve the order of the parameters.
    private readonly List<KeyValuePair<string, StringValues>> _values = [];

    public QueryStringParameterCollection()
    {
    }

    public StringValues this[string name]
    {
        get => Get(name);
        set => Set(name, value);
    }

    public int Count => _values.Count;

    public bool IsEmpty => Count is 0;

    public StringValues Get(string name)
    {
        foreach (var parameter in _values)
        {
            if (parameter.Key == name)
            {
                return parameter.Value;
            }
        }

        return StringValues.Empty;
    }

    public void Append(string name, StringValues values)
    {
        var span = CollectionsMarshal.AsSpan(_values);
        for (var i = 0; i < span.Length; i++)
        {
            var value = span[i];
            if (value.Key == name)
            {
                var newValue = new string[value.Value.Count + values.Count];
                value.Value.ToArray().AsSpan().CopyTo(newValue);
                values.ToArray().AsSpan().CopyTo(newValue.AsSpan(value.Value.Count));
                span[i] = KeyValuePair.Create(name, new StringValues(newValue));
                return;
            }
        }

        _values.Add(KeyValuePair.Create(name, values));
    }

    public void Append(string name, string? value)
    {
        Append(name, new StringValues(value));
    }

    public bool Remove(string name)
    {
        var span = CollectionsMarshal.AsSpan(_values);
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i].Key == name)
            {
                _values.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public void Set(string name, StringValues values)
    {
        var span = CollectionsMarshal.AsSpan(_values);
        for (var i = 0; i < span.Length; i++)
        {
            var value = span[i];
            if (value.Key == name)
            {
                span[i] = KeyValuePair.Create(name, values);
                return;
            }
        }

        _values.Add(KeyValuePair.Create(name, values));
    }

    public void Clear()
    {
        _values.Clear();
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
