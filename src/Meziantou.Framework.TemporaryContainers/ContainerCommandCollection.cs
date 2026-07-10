using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An ordered collection of command tokens (used for the entrypoint or command of a container).</summary>
public sealed class ContainerCommandCollection : IEnumerable<string>
{
    private readonly List<string> _values;

    internal ContainerCommandCollection()
    {
        _values = [];
    }

    internal ContainerCommandCollection(ContainerCommandCollection other)
    {
        _values = [.. other._values];
    }

    /// <summary>Gets the number of tokens in the collection.</summary>
    public int Count => _values.Count;

    /// <summary>Appends a token.</summary>
    /// <param name="value">The token to append.</param>
    public void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _values.Add(value);
    }

    /// <summary>Appends multiple tokens.</summary>
    /// <param name="values">The tokens to append.</param>
    public void AddRange(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values.AddRange(values);
    }

    /// <summary>Removes all tokens.</summary>
    public void Clear() => _values.Clear();

    /// <summary>Returns an enumerator over the tokens.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
