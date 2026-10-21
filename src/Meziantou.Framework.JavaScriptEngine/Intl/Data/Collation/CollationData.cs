namespace Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

/// <summary>
/// The collation data of CLDR (Intl/Data/collation.bin): the root collation (<see cref="CollationRootTable"/>) and the
/// tailorings of the locales (the rules of common/collation/*.xml, with the imports inlined and the comments removed;
/// they are parsed on demand by <see cref="CollationTailoring.Parse"/>, and applied to the root table by the collator).
/// Chunk 0 is the index:
/// <code>
/// keyCount key* ucaVersion rootChunk
/// tailoringCount (locale chunk table)*      (a table per locale: collation type = rules, "_default" = default type)
/// parentCount (locale parent)*              (parentLocales.json: "collations", and the main ones to a non-root locale)
/// </code>
/// </summary>
internal sealed class CollationData
{
    private readonly CldrChunkedFile _file;
    private readonly Func<CldrSupplemental> _supplemental;
    private readonly CldrKeys _keys;
    private readonly int _rootChunk;
    private readonly Dictionary<string, (int Chunk, int Table)> _tailorings;
    private readonly Dictionary<string, string> _parents;
    private readonly CldrTreeChunk?[] _chunks;
    private CollationRootTable? _root;

    internal CollationData(CldrChunkedFile file, Func<CldrSupplemental> supplemental)
    {
        _file = file;
        _supplemental = supplemental;
        var reader = new CldrBinaryReader(file.GetChunk(0), 0);
        _keys = CldrKeys.Read(ref reader);
        UcaVersion = reader.ReadString();
        _rootChunk = reader.ReadVarInt32();
        var count = reader.ReadVarInt32();
        _tailorings = new Dictionary<string, (int, int)>(count, StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            _tailorings.Add(name, (reader.ReadVarInt32(), reader.ReadVarInt32()));
            names.Add(name);
        }

        TailoredLocales = names.AsReadOnly();
        count = reader.ReadVarInt32();
        _parents = new Dictionary<string, string>(count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            _parents.Add(reader.ReadString(), reader.ReadString());
        }

        _chunks = new CldrTreeChunk?[file.ChunkCount];
    }

    /// <summary>Gets the version of UCA of the root collation (e.g. "17.0.0").</summary>
    public string UcaVersion { get; }

    /// <summary>Gets the root collation, decoded on first use.</summary>
    public CollationRootTable Root => Volatile.Read(ref _root) ?? CldrData.InitializeOnce(ref _root, new CollationRootTable(_file.GetChunk(_rootChunk)));

    /// <summary>Gets the locales that have tailorings ("und" is the root, whose tailorings are search, emoji, eor...), sorted by ordinal.</summary>
    public IReadOnlyList<string> TailoredLocales { get; }

    /// <summary>Gets the tailorings of a locale (collation type = rules, and "_default" = the default type), without inheritance.</summary>
    public CldrTable? GetOwnTailorings(string locale)
    {
        if (!_tailorings.TryGetValue(locale, out var location))
            return null;

        var chunk = Volatile.Read(ref _chunks[location.Chunk]);
        if (chunk is null)
        {
            chunk = new CldrTreeChunk(_keys, _file.GetChunk(location.Chunk));
            chunk = Interlocked.CompareExchange(ref _chunks[location.Chunk], chunk, comparand: null) ?? chunk;
        }

        return chunk.GetTable(location.Table);
    }

    /// <summary>
    /// Gets the locales whose tailorings apply to a locale, from the most specific to the root ("und"): the locale, its
    /// collation parent (parentLocales "collations", e.g. "yue" -> "zh-Hant") or its truncation (a region implying
    /// another script is looked up with that script, e.g. "zh-TW" as "zh-Hant-TW"; a script is truncated too, e.g.
    /// "zh-Hant" inherits the tailorings of "zh", as in ICU).
    /// </summary>
    public IReadOnlyList<string> GetFallbackChain(LanguageId locale)
    {
        var likely = _supplemental().LikelySubtags;
        var result = new List<string>();
        var current = locale;
        if (!current.HasScript && current.HasRegion && !current.IsUnknownLanguage)
        {
            var languageScript = likely.AddLikelySubtags(new LanguageId(current.Language, "", "")).Script;
            var regionScript = likely.AddLikelySubtags(new LanguageId(current.Language, "", current.Region)).Script;
            if (!string.Equals(languageScript, regionScript, StringComparison.Ordinal) && regionScript.Length > 0)
            {
                current = current.WithScript(regionScript);
            }
        }

        while (true)
        {
            var name = current.ToString();
            if (_tailorings.ContainsKey(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }

            if (current.IsUnknownLanguage && !current.HasScript && !current.HasRegion && !current.HasVariants)
                break;

            if (_parents.TryGetValue(name, out var parent))
            {
                current = LanguageId.Parse(parent);
            }
            else if (current.HasVariants)
            {
                current = current.WithoutVariants();
            }
            else if (current.HasRegion)
            {
                current = current.WithRegion("");
            }
            else if (current.HasScript && !current.IsUnknownLanguage)
            {
                // The collations use truncation inheritance (parentLocales component="collations" lists the exceptions):
                // "zh-Hant" inherits the tailorings of "zh", as in ICU
                current = current.WithScript("");
            }
            else
            {
                current = new LanguageId("und", "", "");
            }
        }

        return result;
    }

    /// <summary>Gets the rules of a collation type for a locale (with inheritance), "" for the root "standard", or null when the type does not exist.</summary>
    public string? GetRules(LanguageId locale, string type)
    {
        foreach (var name in GetFallbackChain(locale))
        {
            if (GetOwnTailorings(name) is { } table && table.TryGetString(type, out var rules))
                return rules;
        }

        return type is "standard" ? "" : null;
    }

    /// <summary>Gets the collation types of a locale (with inheritance, including "standard"), sorted by ordinal.</summary>
    public IReadOnlyList<string> GetCollationTypes(LanguageId locale)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal) { "standard" };
        foreach (var name in GetFallbackChain(locale))
        {
            if (GetOwnTailorings(name) is not { } table)
                continue;

            for (var i = 0; i < table.Count; i++)
            {
                var key = table.GetKey(i);
                if (key is not "_default")
                {
                    result.Add(key);
                }
            }
        }

        return [.. result];
    }

    /// <summary>Gets the default collation type of a locale (e.g. "pinyin" for "zh", "stroke" for "zh-Hant"), "standard" for most locales.</summary>
    public string GetDefaultCollation(LanguageId locale)
    {
        foreach (var name in GetFallbackChain(locale))
        {
            if (GetOwnTailorings(name) is { } table && table.TryGetString("_default", out var value))
                return value;
        }

        return "standard";
    }
}
