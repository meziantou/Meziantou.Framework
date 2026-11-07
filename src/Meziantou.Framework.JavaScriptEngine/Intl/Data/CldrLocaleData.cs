namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>
/// The locale data of CLDR (Intl/Data/locales.bin). Chunk 0 is the index, decompressed on first use:
/// <code>
/// keyCount key*                                   (sorted by ordinal: the id of a key is its index)
/// localeCount (name parentIndex+1)*               (sorted by ordinal, "und" is the root)
/// aliasCount (name localeIndex)*                  (default content locales, e.g. "fr-FR" -> "fr", and "zh-TW" -> "zh-Hant-TW")
/// categoryCount (name (chunk table)[localeCount])* (chunk 0 when the locale has no data of its own in the category)
/// </code>
/// The other chunks are data trees (<see cref="CldrTreeChunk"/>), one per category and language.
/// </summary>
internal sealed class CldrLocaleData
{
    private readonly CldrChunkedFile _file;
    private readonly CldrKeys _keys;
    private readonly CldrLocale[] _locales;
    private readonly Dictionary<string, CldrLocale> _localesByName;
    private readonly (int Chunk, int Table)[][] _roots;
    private readonly CldrTreeChunk?[] _chunks;
    private readonly Func<CldrSupplemental> _supplemental;

    internal CldrLocaleData(CldrChunkedFile file, Func<CldrSupplemental> supplemental)
    {
        _file = file;
        _supplemental = supplemental;
        var reader = new CldrBinaryReader(file.GetChunk(0), 0);
        _keys = CldrKeys.Read(ref reader);

        var localeCount = reader.ReadVarInt32();
        _locales = new CldrLocale[localeCount];
        var parents = new int[localeCount];
        for (var i = 0; i < localeCount; i++)
        {
            var name = reader.ReadString();
            parents[i] = reader.ReadVarInt32() - 1;
            _locales[i] = new CldrLocale(this, name, i);
        }

        _localesByName = new Dictionary<string, CldrLocale>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < localeCount; i++)
        {
            _locales[i].Parent = parents[i] < 0 ? null : _locales[parents[i]];
            _localesByName.Add(_locales[i].Name, _locales[i]);
            if (parents[i] < 0)
            {
                Root = _locales[i];
            }
        }

        var available = new List<string>();
        foreach (var locale in _locales)
        {
            if (!locale.IsRoot)
            {
                available.Add(locale.Name);
            }
        }

        var aliasCount = reader.ReadVarInt32();
        for (var i = 0; i < aliasCount; i++)
        {
            var name = reader.ReadString();
            _localesByName.Add(name, _locales[reader.ReadVarInt32()]);
            available.Add(name);
        }

        available.Sort(StringComparer.Ordinal);
        AvailableLocales = available.AsReadOnly();

        var categoryCount = reader.ReadVarInt32();
        if (categoryCount != Enum.GetValues<CldrCategory>().Length)
            throw new InvalidDataException("The categories of the locale data do not match the engine");

        _roots = new (int, int)[categoryCount][];
        for (var category = 0; category < categoryCount; category++)
        {
            var name = reader.ReadString();
            if (!string.Equals(name, ((CldrCategory)category).ToString(), StringComparison.Ordinal))
                throw new InvalidDataException("The categories of the locale data do not match the engine: " + name);

            var roots = new (int, int)[localeCount];
            for (var i = 0; i < localeCount; i++)
            {
                roots[i] = (reader.ReadVarInt32(), reader.ReadVarInt32());
            }

            _roots[category] = roots;
        }

        _chunks = new CldrTreeChunk?[file.ChunkCount];
        if (Root is null)
            throw new InvalidDataException("The locale data has no root");
    }

    /// <summary>Gets the root locale ("und").</summary>
    public CldrLocale Root { get; }

    /// <summary>Gets the locales that have data (each one may store nothing of its own in a category), with the root.</summary>
    public IReadOnlyList<CldrLocale> Locales => _locales;

    /// <summary>
    /// Gets the available locales of ECMA-402, sorted by ordinal: the locales of CLDR coverage modern and moderate,
    /// their parents and their aliases (the default content locales such as "fr-FR", and the regions implying a script
    /// such as "zh-TW"), without the root.
    /// </summary>
    public IReadOnlyList<string> AvailableLocales { get; }

    internal CldrKeys Keys => _keys;

    /// <summary>Finds an available locale or alias by name, ignoring the case (e.g. "fr-CA", "zh-TW").</summary>
    public bool TryGetLocale(string name, [NotNullWhen(true)] out CldrLocale? locale) => _localesByName.TryGetValue(name, out locale);

    /// <summary>Gets whether a locale is available (see <see cref="AvailableLocales"/>).</summary>
    public bool IsAvailableLocale(string name) => _localesByName.TryGetValue(name, out var locale) && !locale.IsRoot;

    /// <summary>
    /// Finds the locale whose data to use for a language identifier: the locale itself or its alias, else its fallback
    /// (a region implying another script is looked up with that script, e.g. "zh-MO" as "zh-Hant-MO"; then the
    /// variants, the region and the script are removed in turn; a script that is not the likely one of the language
    /// falls back to the root, as the parentLocales rule "nonlikelyScript" of CLDR).
    /// </summary>
    public CldrLocale FindLocale(LanguageId id)
    {
        if (TryGetLocale(id.ToString(), out var locale))
            return locale;

        var likely = _supplemental().LikelySubtags;
        if (!id.HasScript && id.HasRegion && !id.IsUnknownLanguage)
        {
            var languageScript = likely.AddLikelySubtags(new LanguageId(id.Language, "", "")).Script;
            var regionScript = likely.AddLikelySubtags(new LanguageId(id.Language, "", id.Region)).Script;
            if (!string.Equals(languageScript, regionScript, StringComparison.Ordinal) && regionScript.Length > 0)
            {
                id = id.WithScript(regionScript);
                if (TryGetLocale(id.ToString(), out locale))
                    return locale;
            }
        }

        while (true)
        {
            if (id.HasVariants)
            {
                id = id.WithoutVariants();
            }
            else if (id.HasRegion)
            {
                id = id.WithRegion("");
            }
            else if (id.HasScript)
            {
                var languageScript = likely.AddLikelySubtags(new LanguageId(id.Language, "", "")).Script;
                if (!string.Equals(languageScript, id.Script, StringComparison.Ordinal))
                    return Root;

                id = id.WithScript("");
            }
            else
            {
                return Root;
            }

            if (TryGetLocale(id.ToString(), out locale))
                return locale;
        }
    }

    /// <summary>Gets the indexes of the chunks of a category, sorted.</summary>
    internal IReadOnlyList<int> GetChunkIndexes(CldrCategory category)
    {
        var result = new SortedSet<int>();
        foreach (var (chunk, _) in _roots[(int)category])
        {
            if (chunk != 0)
            {
                result.Add(chunk);
            }
        }

        return [.. result];
    }

    /// <summary>Gets the root table of the own data of a locale in a category, or null when it has none.</summary>
    internal CldrTable? GetOwnTable(CldrCategory category, int localeIndex)
    {
        var (chunkIndex, table) = _roots[(int)category][localeIndex];
        if (chunkIndex == 0)
            return null;

        var chunk = Volatile.Read(ref _chunks[chunkIndex]);
        if (chunk is null)
        {
            chunk = new CldrTreeChunk(_keys, _file.GetChunk(chunkIndex));
            chunk = Interlocked.CompareExchange(ref _chunks[chunkIndex], chunk, comparand: null) ?? chunk;
        }

        return chunk.GetTable(table);
    }
}
