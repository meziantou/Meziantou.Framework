using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The computed values that do not depend on the element they are computed for (e.g. <c>0px</c>, <c>10pt</c>, <c>rgb(0 0 0)</c>), by
/// property, specified value and base URL: most elements share a few specified values (the initial values, the values of the
/// rules), which are tokenized and serialized once. The cache is cleared when it is full: the values that result from a
/// substitution (e.g. an animated custom property) may be all different.
/// </summary>
internal sealed class ContextFreeValueCache
{
    private const int MaxCount = 8192;

    private readonly Dictionary<Key, string> _values = [];

    public bool TryGetValue(CssPropertyDefinition property, string value, string? baseUrl, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string result)
        => _values.TryGetValue(new Key(property, value, GetRelevantBaseUrl(value, baseUrl)), out result);

    public void Add(CssPropertyDefinition property, string value, string? baseUrl, string result)
    {
        if (_values.Count >= MaxCount)
        {
            _values.Clear();
        }

        _values[new Key(property, value, GetRelevantBaseUrl(value, baseUrl))] = result;
    }

    /// <summary>The base URL only matters to a value that may contain a URL (a function or a string, e.g. url() or image-set()).</summary>
    private static string? GetRelevantBaseUrl(string value, string? baseUrl) => baseUrl is not null && value.AsSpan().IndexOfAny('(', '"', '\'') >= 0 ? baseUrl : null;

    private readonly struct Key(CssPropertyDefinition property, string value, string? baseUrl) : IEquatable<Key>
    {
        private readonly CssPropertyDefinition _property = property;
        private readonly string _value = value;
        private readonly string? _baseUrl = baseUrl;

        public bool Equals(Key other) => ReferenceEquals(_property, other._property) && string.Equals(_value, other._value, StringComparison.Ordinal) && string.Equals(_baseUrl, other._baseUrl, StringComparison.Ordinal);

        public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_property), string.GetHashCode(_value, StringComparison.Ordinal), _baseUrl is null ? 0 : string.GetHashCode(_baseUrl, StringComparison.Ordinal));
    }
}
