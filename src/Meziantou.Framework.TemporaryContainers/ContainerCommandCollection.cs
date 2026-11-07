using System.Collections;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>An ordered collection of command tokens (used for the entrypoint or command of a container).</summary>
public sealed class ContainerCommandCollection : IEnumerable<string>
{
    private readonly List<string> _values;
    private bool _isReadOnly;

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
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        _values.Add(value);
    }

    /// <summary>Appends multiple tokens.</summary>
    /// <param name="values">The tokens to append.</param>
    public void AddRange(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        _values.AddRange(values);
    }

    /// <summary>Removes all tokens.</summary>
    public void Clear()
    {
        DefinitionReadOnly.ThrowIf(_isReadOnly);
        _values.Clear();
    }

    /// <summary>Returns an enumerator over the tokens.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal void MakeReadOnly() => _isReadOnly = true;
}
