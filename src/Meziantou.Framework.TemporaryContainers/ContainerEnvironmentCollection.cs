using System.Collections;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A collection of environment variables passed to a container.</summary>
public sealed class ContainerEnvironmentCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _variables;

    internal ContainerEnvironmentCollection()
    {
        _variables = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal ContainerEnvironmentCollection(ContainerEnvironmentCollection other)
    {
        _variables = new Dictionary<string, string>(other._variables, StringComparer.Ordinal);
    }

    /// <summary>Gets the number of environment variables in the collection.</summary>
    public int Count => _variables.Count;

    /// <summary>Adds or replaces an environment variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    public void Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    /// <summary>Removes an environment variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns><see langword="true"/> if the variable was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.Remove(name);
    }

    /// <summary>Determines whether an environment variable is defined.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns><see langword="true"/> if the variable is defined; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.ContainsKey(name);
    }

    /// <summary>Gets the value of an environment variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <see langword="null"/> if the variable is not defined.</returns>
    public string? GetValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _variables.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Returns an enumerator over the environment variables.</summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _variables.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
