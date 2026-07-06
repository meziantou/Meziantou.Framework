using System.Text.RegularExpressions;
using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Schemas;

/// <summary>Base implementation for a based schema.</summary>
public abstract class SchemaBase : IYamlSchema
{
    private readonly Lock _lock = new Lock();

    private readonly Dictionary<string, string> _shortTagToLongTag = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _longTagToShortTag = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<ScalarResolutionRule> _scalarTagResolutionRules = new List<ScalarResolutionRule>();
    private readonly Dictionary<string, Regex> _algorithms = new Dictionary<string, Regex>(StringComparer.Ordinal);

    private readonly Dictionary<string, List<ScalarResolutionRule>> _mapTagToScalarResolutionRuleList = new Dictionary<string, List<ScalarResolutionRule>>(StringComparer.Ordinal);

    private readonly Dictionary<Type, List<ScalarResolutionRule>> _mapTypeToScalarResolutionRuleList = new Dictionary<Type, List<ScalarResolutionRule>>();

    private readonly Dictionary<Type, string> _mapTypeToShortTag = new Dictionary<Type, string>();
    private readonly Dictionary<string, Type> _mapShortTagToType = new Dictionary<string, Type>(StringComparer.Ordinal);

    private int _updateCounter;
    private bool _needFirstUpdate = true;

    /// <summary>
    /// The string short tag: !!str
    /// </summary>
    public const string StrShortTag = "!!str";

    /// <summary>
    /// The string long tag: tag:yaml.org,2002:str
    /// </summary>
    public const string StrLongTag = "tag:yaml.org,2002:str";

    /// <summary>Expands a short tag into its long form.</summary>
    [return: NotNullIfNotNull("shortTag")]
    public string? ExpandTag(string? shortTag)
    {
        if (shortTag == null)
            return null;

        return _shortTagToLongTag.TryGetValue(shortTag, out var tagExpanded) ? tagExpanded : shortTag;
    }

    /// <summary>Converts a long tag into its short form.</summary>
    [return: NotNullIfNotNull(nameof(tag))]
    public string? ShortenTag(string? tag)
    {
        if (tag is null)
            return null;

        return _longTagToShortTag.TryGetValue(tag, out var tagShortened) ? tagShortened : tag;
    }

    /// <summary>Gets default Tag.</summary>
    public string? GetDefaultTag(NodeEvent nodeEvent)
    {
        EnsureScalarRules();

        ArgumentNullException.ThrowIfNull(nodeEvent);

        if (nodeEvent is MappingStart mapping)
        {
            return GetDefaultTag(mapping);
        }

        if (nodeEvent is SequenceStart sequence)
        {
            return GetDefaultTag(sequence);
        }

        if (nodeEvent is Scalar scalar)
        {
            TryParse(scalar, false, out var tag, out _);
            return tag;
        }

        throw new NotSupportedException($"NodeEvent [{nodeEvent.GetType().FullName}] not supported");
    }

    /// <summary>Gets default Tag.</summary>
    public string? GetDefaultTag(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnsureScalarRules();

        _mapTypeToShortTag.TryGetValue(type, out var defaultTag);
        return defaultTag;
    }

    /// <summary>Determines whether tag Implicit.</summary>
    public bool IsTagImplicit(string? tag)
    {
        if (tag == null)
        {
            return true;
        }
        return _shortTagToLongTag.ContainsKey(tag);
    }

    /// <summary>Registers a long/short tag association.</summary>
    /// <param name="shortTag">The short tag.</param>
    /// <param name="longTag">The long tag.</param>
    /// <exception cref="ArgumentNullException">
    /// shortTag
    /// or
    /// shortTag
    /// </exception>
    public void RegisterTag(string shortTag, string longTag)
    {
        ArgumentNullException.ThrowIfNull(shortTag);
        ArgumentNullException.ThrowIfNull(longTag);

        _shortTagToLongTag[shortTag] = longTag;
        _longTagToShortTag[longTag] = shortTag;
    }

    /// <summary>
    /// Gets the default tag for a <see cref="MappingStart"/> event.
    /// </summary>
    /// <param name="nodeEvent">The node event.</param>
    /// <returns>The default tag for a map.</returns>
    protected abstract string GetDefaultTag(MappingStart nodeEvent);

    /// <summary>
    /// Gets the default tag for a <see cref="SequenceStart"/> event.
    /// </summary>
    /// <param name="nodeEvent">The node event.</param>
    /// <returns>The default tag for a seq.</returns>
    protected abstract string GetDefaultTag(SequenceStart nodeEvent);

    /// <summary>Tries to parse.</summary>
    public virtual bool TryParse(Scalar scalar, bool decodeValue, [NotNullWhen(true)] out string? defaultTag, out object? value)
    {
        ArgumentNullException.ThrowIfNull(scalar);

        EnsureScalarRules();

        defaultTag = null;
        value = null;

        // DoubleQuoted and SingleQuoted string are always decoded
        if (scalar.Style == ScalarStyle.DoubleQuoted || scalar.Style == ScalarStyle.SingleQuoted)
        {
            defaultTag = StrShortTag;
            if (decodeValue)
            {
                value = scalar.Value;
            }
            return true;
        }

        // Parse only values if we have some rules
        if (_scalarTagResolutionRules.Count > 0)
        {
            foreach (var rule in _scalarTagResolutionRules)
            {
                var match = rule.Pattern.Match(scalar.Value);
                if (!match.Success)
                    continue;

                defaultTag = rule.Tag;
                if (decodeValue)
                {
                    value = rule.Decode(match);
                }
                return true;
            }
        }
        else
        {
            // Expand the tag to a default tag.
            defaultTag = ShortenTag(scalar.Tag);
        }

        // Value was not successfully decoded
        return false;
    }

    /// <summary>Tries to parse.</summary>
    public bool TryParse(Scalar scalar, Type type, out object? value)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        ArgumentNullException.ThrowIfNull(type);

        EnsureScalarRules();

        value = null;

        // DoubleQuoted and SingleQuoted string are always decoded
        if (type == typeof(string) && (scalar.Style == ScalarStyle.DoubleQuoted || scalar.Style == ScalarStyle.SingleQuoted))
        {
            value = scalar.Value;
            return true;
        }

        // Parse only values if we have some rules
        if (_mapTypeToScalarResolutionRuleList.Count > 0)
        {
            if (_mapTypeToScalarResolutionRuleList.TryGetValue(type, out var rules))
            {
                foreach (var rule in rules)
                {
                    var match = rule.Pattern.Match(scalar.Value);
                    if (match.Success)
                    {
                        value = rule.Decode(match);
                        return true;
                    }
                }
            }
        }

        // Value was not successfully decoded
        return false;
    }

    /// <summary>Gets type For Default Tag.</summary>
    public Type? GetTypeForDefaultTag(string? tag)
    {
        if (tag == null)
        {
            return null;
        }

        _mapShortTagToType.TryGetValue(tag, out var type);
        return type;
    }

    /// <summary>Prepare scalar rules. In the implementation of this method, should call</summary>
    protected virtual void PrepareScalarRules()
    {
    }

    /// <summary>
    /// Add a tag resolution rule that is invoked when <paramref name="regex" /> matches
    /// the <see cref="Scalar">Value of</see> a <see cref="Scalar" /> node.
    /// The tag is resolved to <paramref name="tag" /> and <paramref name="decode" /> is
    /// invoked when actual value of type <typeparamref name="T" /> is extracted from
    /// the node text.
    /// </summary>
    /// <typeparam name="T">Type of the scalar</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="regex">The regex.</param>
    /// <param name="decode">The decode function.</param>
    /// <param name="encode">The encode function.</param>
    /// <example>
    ///   <code>
    /// BeginUpdate(); // to avoid invoking slow internal calculation method many times.
    /// Add( ... );
    /// Add( ... );
    /// Add( ... );
    /// Add( ... );
    /// EndUpdate();   // automaticall invoke internal calculation method
    ///   </code></example>
    protected void AddScalarRule<T>(string tag, [StringSyntax(StringSyntaxAttribute.Regex)] string regex, Func<Match, T?> decode, Func<T, string>? encode)
    {
        // Make sure the tag is expanded to its long form
        var longTag = ShortenTag(tag);
        _scalarTagResolutionRules.Add(new ScalarResolutionRule(longTag, regex, m => decode(m), encode is null ? null : m => encode((T)m), typeof(T)));
    }

    /// <summary>Adds a scalar resolution rule for one or more CLR types.</summary>
    protected void AddScalarRule(Type[] types, string tag, [StringSyntax(StringSyntaxAttribute.Regex)] string regex, Func<Match, object?> decode, Func<object, string>? encode)
    {
        // Make sure the tag is expanded to its long form
        var longTag = ShortenTag(tag);
        _scalarTagResolutionRules.Add(new ScalarResolutionRule(longTag, regex, decode, encode, types));
    }

    /// <summary>Registers a default tag mapping for <typeparamref name="T"/>.</summary>
    protected void RegisterDefaultTagMapping<T>(string tag, bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(tag);
        RegisterDefaultTagMapping(tag, typeof(T), isDefault);
    }

    /// <summary>Registers a default tag mapping for the specified CLR type.</summary>
    protected void RegisterDefaultTagMapping(string tag, Type type, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(type);

        _mapTypeToShortTag.TryAdd(type, tag);
        if (isDefault)
        {
            _mapShortTagToType[tag] = type;
        }
    }

    private void EnsureScalarRules()
    {
        lock (_lock)
        {
            if (_needFirstUpdate || _updateCounter != _scalarTagResolutionRules.Count)
            {
                PrepareScalarRules();
                Update();
                _needFirstUpdate = false;
            }
        }
    }

    private void Update()
    {
        // Tag to joined regexp source
        var mapTagToPartialRegexPattern = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in _scalarTagResolutionRules)
        {
            if (!mapTagToPartialRegexPattern.ContainsKey(rule.Tag))
            {
                mapTagToPartialRegexPattern.Add(rule.Tag, rule.PatternSource);
            }
            else
            {
                mapTagToPartialRegexPattern[rule.Tag] += "|" + rule.PatternSource;
            }
        }

        // Tag to joined regexp
        _algorithms.Clear();
        foreach (var entry in mapTagToPartialRegexPattern)
        {
            _algorithms.Add(
                entry.Key,
                new Regex("^(" + entry.Value + ")$", RegexOptions.None, Timeout.InfiniteTimeSpan)
            );
        }

        // Tag to decoding methods
        _mapTagToScalarResolutionRuleList.Clear();
        foreach (var rule in _scalarTagResolutionRules)
        {
            if (!_mapTagToScalarResolutionRuleList.ContainsKey(rule.Tag))
                _mapTagToScalarResolutionRuleList[rule.Tag] = new List<ScalarResolutionRule>();
            _mapTagToScalarResolutionRuleList[rule.Tag].Add(rule);
        }

        _mapTypeToScalarResolutionRuleList.Clear();
        foreach (var rule in _scalarTagResolutionRules)
        {
            var types = rule.GetTypeOfValue();
            foreach (var type in types)
            {
                if (!_mapTypeToScalarResolutionRuleList.ContainsKey(type))
                    _mapTypeToScalarResolutionRuleList[type] = new List<ScalarResolutionRule>();
                _mapTypeToScalarResolutionRuleList[type].Add(rule);
            }
        }

        // Update the counter
        _updateCounter = _scalarTagResolutionRules.Count;
    }

    private class ScalarResolutionRule
    {
        public ScalarResolutionRule(string shortTag, [StringSyntax(StringSyntaxAttribute.Regex)] string regex, Func<Match, object?> decoder, Func<object, string>? encoder, params Type[] types)
        {
            Tag = shortTag;
            PatternSource = regex;
            Pattern = new Regex("^(?:" + regex + ")$", RegexOptions.None, Timeout.InfiniteTimeSpan);
            this._types = types;
            _decoder = decoder;
            _encoder = encoder;
        }

        private readonly Type[] _types;
        private readonly Func<Match, object?> _decoder;
        private readonly Func<object, string>? _encoder;

        public string Tag { get; protected set; }
        public Regex Pattern { get; protected set; }
        public string PatternSource { get; protected set; }

        public object? Decode(Match m)
        {
            return _decoder(m);
        }

        public string Encode(object obj)
        {
            if (_encoder is null)
            {
                throw new InvalidOperationException("This scalar resolution rule does not define an encoder.");
            }

            return _encoder(obj);
        }

        public Type[] GetTypeOfValue()
        {
            return _types;
        }

        public bool HasEncoder()
        {
            return _encoder != null;
        }

        public bool IsMatch(string value)
        {
            return Pattern.IsMatch(value);
        }
    }
}
