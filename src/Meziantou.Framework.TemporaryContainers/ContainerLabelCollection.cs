using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of labels applied to a container.</summary>
public sealed class ContainerLabelCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _labels;

    internal ContainerLabelCollection()
    {
        _labels = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal ContainerLabelCollection(ContainerLabelCollection other)
    {
        _labels = new Dictionary<string, string>(other._labels, StringComparer.Ordinal);
    }

    /// <summary>Gets the number of labels in the collection.</summary>
    public int Count => _labels.Count;

    /// <summary>Adds or replaces a label.</summary>
    /// <param name="name">The label name.</param>
    /// <param name="value">The label value.</param>
    public void Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _labels[name] = value;
    }

    /// <summary>Removes a label.</summary>
    /// <param name="name">The label name.</param>
    /// <returns><see langword="true"/> if the label was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _labels.Remove(name);
    }

    /// <summary>Determines whether a label is defined.</summary>
    /// <param name="name">The label name.</param>
    /// <returns><see langword="true"/> if the label is defined; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _labels.ContainsKey(name);
    }

    /// <summary>Returns an enumerator over the labels.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _labels.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
