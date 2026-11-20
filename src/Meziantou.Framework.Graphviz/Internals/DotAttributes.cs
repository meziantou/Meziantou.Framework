namespace Meziantou.Framework.Graphviz.Internals;

internal sealed class DotAttributes
{
    private readonly Dictionary<string, DotAttribute> _values;

    public DotAttributes()
    {
        _values = new Dictionary<string, DotAttribute>(StringComparer.Ordinal);
    }

    private DotAttributes(Dictionary<string, DotAttribute> values)
    {
        _values = values;
    }

    public int Count => _values.Count;

    public IEnumerable<KeyValuePair<string, DotAttribute>> Values => _values;

    public void Set(string name, DotAttribute value) => _values[name] = value;

    public void SetAll(DotAttributes attributes)
    {
        foreach (var (name, value) in attributes._values)
        {
            _values[name] = value;
        }
    }

    public DotAttribute? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    /// <summary>Gets the value of an attribute, or <see langword="null"/> when it is not set or empty, as Graphviz treats an empty value as the default.</summary>
    public string? GetValue(string name) => _values.TryGetValue(name, out var value) && value.Value.Length > 0 ? value.Value : null;

    public DotAttributes Clone() => new(new Dictionary<string, DotAttribute>(_values, StringComparer.Ordinal));

    public DotAttributes CloneInherited()
    {
        var values = new Dictionary<string, DotAttribute>(_values.Count, StringComparer.Ordinal);
        foreach (var (name, value) in _values)
        {
            values[name] = value with { IsInherited = true };
        }

        return new DotAttributes(values);
    }
}
