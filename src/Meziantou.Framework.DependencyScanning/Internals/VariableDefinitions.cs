namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>The variables or properties defined in a file, which the values of the file can reference.</summary>
/// <remarks>
/// A variable defined more than once, or whose value is unknown, is not resolved: which definition applies depends on
/// conditions or on the order of evaluation. All the dependencies that use a definition share its location, so that one
/// update changes all of them, and the value an update writes is the value the next update expects.
/// </remarks>
internal sealed class VariableDefinitions
{
    private readonly Dictionary<string, Definition?> _definitions;

    public VariableDefinitions(StringComparer comparer)
    {
        _definitions = new(comparer);
    }

    public int Count => _definitions.Count;

    /// <summary>Adds the definition of a variable. A <see langword="null"/> value records that the value of the variable is unknown.</summary>
    public void Add(string name, string? value, Func<Location> createLocation)
    {
        if (value is null || _definitions.ContainsKey(name))
        {
            _definitions[name] = null;
            return;
        }

        _definitions[name] = new Definition(value, createLocation);
    }

    public bool TryGetValue(string name, [NotNullWhen(true)] out string? value, [NotNullWhen(true)] out Location? location)
    {
        if (_definitions.TryGetValue(name, out var definition) && definition is not null)
        {
            value = definition.Value;
            location = definition.GetLocation();
            return true;
        }

        value = null;
        location = null;
        return false;
    }

    /// <summary>Replaces the references of <paramref name="value"/> with the values of the variables.</summary>
    public ResolvedValue Resolve(string value, List<VariableReference> references)
    {
        if (references.Count is 0)
            return new ResolvedValue(value, Location: null, VariableName: null);

        if (references is [{ Start: 0, Name: { } name } reference] && reference.Length == value.Length)
        {
            return TryGetValue(name, out var variableValue, out var location)
                ? new ResolvedValue(variableValue, location, name)
                : new ResolvedValue(null, null, name);
        }

        var result = new StringBuilder(value.Length);
        var index = 0;
        foreach (var item in references)
        {
            if (item.Name is null || !TryGetValue(item.Name, out var variableValue, out _))
                return default;

            result.Append(value, index, item.Start - index).Append(variableValue);
            index = item.Start + item.Length;
        }

        result.Append(value, index, value.Length - index);
        return new ResolvedValue(result.ToString(), Location: null, VariableName: null);
    }

    private sealed class Definition(string value, Func<Location> createLocation)
    {
        private Location? _location;

        public string Value { get; } = value;

        public Location GetLocation() => _location ??= createLocation();
    }
}
