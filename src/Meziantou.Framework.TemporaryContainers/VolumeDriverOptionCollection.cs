using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of driver-specific options applied to a volume.</summary>
public sealed class VolumeDriverOptionCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _options;

    internal VolumeDriverOptionCollection()
    {
        _options = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal VolumeDriverOptionCollection(VolumeDriverOptionCollection other)
    {
        _options = new Dictionary<string, string>(other._options, StringComparer.Ordinal);
    }

    /// <summary>Gets the number of options in the collection.</summary>
    public int Count => _options.Count;

    /// <summary>Adds or replaces an option.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="value">The option value.</param>
    public void Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _options[name] = value;
    }

    /// <summary>Removes an option.</summary>
    /// <param name="name">The option name.</param>
    /// <returns><see langword="true"/> if the option was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _options.Remove(name);
    }

    /// <summary>Determines whether an option is defined.</summary>
    /// <param name="name">The option name.</param>
    /// <returns><see langword="true"/> if the option is defined; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _options.ContainsKey(name);
    }

    /// <summary>Returns an enumerator over the options.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _options.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
