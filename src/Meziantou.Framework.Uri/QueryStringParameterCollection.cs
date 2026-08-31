using System.Collections;
using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework;

/// <summary>Represents a collection of query string parameters that preserves insertion order and allows duplicate keys with multiple values.</summary>
public sealed class QueryStringParameterCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    // The number of parameters is often small, so we use a List instead of a Dictionary.
    // Also, we want to preserve the order of the parameters.
    private readonly List<Parameter> _values = [];

    /// <summary>Initializes a new instance of the <see cref="QueryStringParameterCollection"/> class.</summary>
    public QueryStringParameterCollection()
    {
    }

    /// <summary>Gets or sets the parameter value with the specified name.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <returns>The value associated with the specified name.</returns>
    public StringValues this[string name]
    {
        get => Get(name);
        set => Set(name, value);
    }

    /// <summary>Gets the number of parameters in the collection.</summary>
    public int Count => _values.Count;

    /// <summary>Gets a value indicating whether the collection is empty.</summary>
    public bool IsEmpty => Count is 0;

    /// <summary>Gets the value associated with the specified parameter name.</summary>
    /// <param name="name">The name of the parameter to get.</param>
    /// <returns>The value associated with the specified name, or <see cref="StringValues.Empty"/> if not found.</returns>
    public StringValues Get(string name)
    {
        var parameter = Find(name);
        if (parameter is null)
            return StringValues.Empty;

        return parameter.GetValues();
    }

    /// <summary>Appends the specified values to an existing parameter or adds a new parameter if it doesn't exist.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="values">The values to append.</param>
    public void Append(string name, StringValues values)
    {
        var parameter = Find(name);
        if (parameter is null)
        {
            _values.Add(new Parameter(name, values));
            return;
        }

        parameter.Append(values);
    }

    /// <summary>Appends the specified value to an existing parameter or adds a new parameter if it doesn't exist.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value to append.</param>
    public void Append(string name, string? value)
    {
        Append(name, new StringValues(value));
    }

    /// <summary>Removes the parameter with the specified name from the collection.</summary>
    /// <param name="name">The name of the parameter to remove.</param>
    /// <returns><see langword="true"/> if the parameter was found and removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        for (var i = 0; i < _values.Count; i++)
        {
            if (_values[i].Name == name)
            {
                _values.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Sets the value of a parameter, replacing any existing values, or adds the parameter if it doesn't exist.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="values">The values to set.</param>
    public void Set(string name, StringValues values)
    {
        var parameter = Find(name);
        if (parameter is null)
        {
            _values.Add(new Parameter(name, values));
            return;
        }

        parameter.SetValues(values);
    }

    /// <summary>Removes all parameters from the collection.</summary>
    public void Clear()
    {
        _values.Clear();
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    /// <returns>An enumerator for the collection.</returns>
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        foreach (var parameter in _values)
        {
            yield return KeyValuePair.Create(parameter.Name, parameter.GetValues());
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Parameter? Find(string name)
    {
        foreach (var parameter in _values)
        {
            if (parameter.Name == name)
                return parameter;
        }

        return null;
    }

    /// <summary>
    /// Holds the values of a single parameter. Appended values are buffered in a growable list and merged
    /// into <see cref="StringValues"/> only when they are read, so appending n values to one name costs
    /// O(n) instead of the O(n²) of copying the whole array on every append.
    /// </summary>
    private sealed class Parameter
    {
        private StringValues _values;
        private List<string?>? _appended;

        public Parameter(string name, StringValues values)
        {
            Name = name;
            _values = values;
        }

        public string Name { get; }

        public StringValues GetValues()
        {
            if (_appended is not null)
            {
                var merged = new string?[_values.Count + _appended.Count];
                for (var i = 0; i < _values.Count; i++)
                {
                    merged[i] = _values[i];
                }

                _appended.CopyTo(merged, _values.Count);
                _values = new StringValues(merged);
                _appended = null;
            }

            return _values;
        }

        public void SetValues(StringValues values)
        {
            _values = values;
            _appended = null;
        }

        public void Append(StringValues values)
        {
            _appended ??= [];
            for (var i = 0; i < values.Count; i++)
            {
                _appended.Add(values[i]);
            }
        }
    }
}
