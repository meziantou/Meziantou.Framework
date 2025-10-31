using System.Collections;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework;

/// <summary>
/// Represents a collection of query string parameters that preserves insertion order and allows duplicate keys with multiple values.
/// </summary>
/// <example>
/// <code>
/// var parameters = new QueryStringParameterCollection();
/// parameters.Append("category", "books");
/// parameters.Append("category", "electronics");
/// parameters.Append("sort", "price");
/// 
/// var uri = QueryStringUtilities.AddQueryString("https://example.com", parameters);
/// // Result: https://example.com?category=books&amp;category=electronics&amp;sort=price
/// </code>
/// </example>
public sealed class QueryStringParameterCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    // The number of parameters is often small, so we use a List instead of a Dictionary.
    // Also, we want to preserve the order of the parameters.
    private readonly List<KeyValuePair<string, StringValues>> _values = [];

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
        foreach (var parameter in _values)
        {
            if (parameter.Key == name)
            {
                return parameter.Value;
            }
        }

        return StringValues.Empty;
    }

    /// <summary>Appends the specified values to an existing parameter or adds a new parameter if it doesn't exist.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="values">The values to append.</param>
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

    /// <summary>Sets the value of a parameter, replacing any existing values, or adds the parameter if it doesn't exist.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="values">The values to set.</param>
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

    /// <summary>Removes all parameters from the collection.</summary>
    public void Clear()
    {
        _values.Clear();
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    /// <returns>An enumerator for the collection.</returns>
    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
