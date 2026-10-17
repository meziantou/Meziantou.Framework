using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The automatic hyphenation of a language (https://drafts.csswg.org/css-text-4/#hyphenation): Liang's algorithm
/// (https://tug.org/docs/liang/) with the patterns of hyph-utf8 (https://github.com/hyphenation/tex-hyphen) compiled by
/// <c>tools/Meziantou.Framework.WebPlatform.SpecGen</c> (<c>HyphenationGenerator</c>) into the embedded resources <c>Unicode/Hyphenation/*.hyph</c>, as Chrome
/// does on Android, Linux and ChromeOS, whose dictionaries come from the same patterns.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm and the choices left to the user agent follow Chrome (<c>third_party/blink/renderer/platform/text/hyphenation.cc</c>,
/// <c>hyphenation/hyphenation_minikin.cc</c>, and the hyphenator of Android <c>hyphenation/hyphenator_aosp.cc</c>, Copyright (C) 2015
/// The Android Open Source Project, Apache License 2.0): the language fallbacks, the punctuation around a word, the limits of
/// <c>hyphenate-limit-chars: auto</c> (5 characters in the word, 2 before and 2 after the hyphen, and at least 3 after the
/// hyphen and at most 62 characters in the word for the patterns), and the capitalized words, which are not hyphenated in
/// English. The lengths are in UTF-16 code units, and the words with a character that is not a letter of the patterns are not
/// hyphenated.
/// </para>
/// <para>A dictionary is loaded on first use and is immutable: an instance is shared by the threads.</para>
/// </remarks>
internal sealed partial class Hyphenator
{
    private const string ResourcePrefix = "Meziantou.Framework.WebPlatform.Unicode.Hyphenation.";

    // The limits of the hyphenator of Android (hyphenator_aosp.h): MIN_PREFIX, MIN_SUFFIX, and MAX_HYPHENATED_SIZE minus the
    // two word boundaries
    private const int PatternMinPrefix = 2;
    private const int PatternMinSuffix = 3;
    private const int MaxWordLength = 62;

    // https://drafts.csswg.org/css-text-4/#propdef-hyphenate-limit-chars: the values of auto (Chrome's hyphenation.h)
    private const int DefaultMinWordLength = 5;
    private const int DefaultMinPrefixLength = 2;
    private const int DefaultMinSuffixLength = 2;

    /// <summary>
    /// The language fallbacks of Chrome (<c>HyphenationMinikin::CreateLocaleFallbackMap</c>, from CLDR as compiled by Android): the
    /// dictionary of a locale, and the one used when the locale is exactly the key ("en" is en-us, "en-AU" is en-gb). Serbian is
    /// added (written in Cyrillic by default, CLDR likelySubtags).
    /// </summary>
    private static readonly FrozenDictionary<string, (string Locale, string? ExactMatch)> LocaleFallbacks = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase)
    {
        // The English locales that fall back to en-US; the other English locales are en-gb, except "en" for the interoperability
        ["en-AS"] = ("en-us", null),
        ["en-GU"] = ("en-us", null),
        ["en-MH"] = ("en-us", null),
        ["en-MP"] = ("en-us", null),
        ["en-PR"] = ("en-us", null),
        ["en-UM"] = ("en-us", null),
        ["en-VI"] = ("en-us", null),
        ["en"] = ("en-gb", "en-us"),

        // German: the 1996 orthography by default; Liechtenstein uses the Swiss rules for the 1901 orthography
        ["de"] = ("de-1996", null),
        ["de-LI-1901"] = ("de-ch-1901", null),
        ["no"] = ("nb", null),
        ["mn"] = ("mn-cyrl", null),
        ["sr"] = ("sr-cyrl", null),

        // The Indic patterns are script-based
        ["und-Beng"] = ("bn", null),
        ["und-Deva"] = ("hi", null),
        ["und-Gujr"] = ("gu", null),
        ["und-Guru"] = ("pa", null),
        ["und-Knda"] = ("kn", null),
        ["und-Mlym"] = ("ml", null),
        ["und-Orya"] = ("or", null),
        ["und-Taml"] = ("ta", null),
        ["und-Telu"] = ("te", null),

        // The locales of dictionaries that do not fall back
        ["de-1901"] = ("de-1901", null),
        ["de-1996"] = ("de-1996", null),
        ["de-ch-1901"] = ("de-ch-1901", null),
        ["en-gb"] = ("en-gb", null),
        ["en-us"] = ("en-us", null),
        ["mn-cyrl"] = ("mn-cyrl", null),
        ["sr-cyrl"] = ("sr-cyrl", null),
        ["sr-latn"] = ("sr-latn", null),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly ushort[] _alphabet;
    private readonly byte[] _letters;
    private readonly byte[] _childCounts;
    private readonly int[] _firstChildren;
    private readonly byte[] _edgeLetters;
    private readonly ushort[] _valueOffsets;
    private readonly byte[] _values;

    private Hyphenator(string locale, ReadOnlySpan<byte> data)
    {
        // See HyphenationGenerator for the format
        Locale = locale;
        if (data.Length < 12 || data[0] != 1)
            throw new InvalidDataException($"The hyphenation dictionary '{locale}' is invalid");

        var alphabetLength = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var nodeCount = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var poolLength = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        var position = 12;
        _alphabet = new ushort[alphabetLength];
        for (var i = 0; i < alphabetLength; i++, position += 2)
        {
            _alphabet[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        }

        _letters = data.Slice(position, alphabetLength).ToArray();
        position += alphabetLength;
        _childCounts = data.Slice(position, nodeCount).ToArray();
        position += nodeCount;
        _edgeLetters = data.Slice(position, nodeCount).ToArray();
        position += nodeCount;
        _valueOffsets = new ushort[nodeCount];
        for (var i = 0; i < nodeCount; i++, position += 2)
        {
            _valueOffsets[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        }

        _values = data.Slice(position, poolLength).ToArray();

        // The nodes are in breadth-first order: the children of each node follow the children of the nodes before it
        _firstChildren = new int[nodeCount];
        var next = 1;
        for (var i = 0; i < nodeCount; i++)
        {
            _firstChildren[i] = next;
            next += _childCounts[i];
        }

        // https://github.com/w3c/csswg-drafts/issues/5157: Chrome does not hyphenate the capitalized words in English
        HyphenatesCapitalizedWords = !locale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The locale of the dictionary (e.g. <c>en-us</c>).</summary>
    public string Locale { get; }

    /// <summary>Whether the words that start with an uppercase letter are hyphenated.</summary>
    public bool HyphenatesCapitalizedWords { get; }

    /// <summary>
    /// Returns the hyphenator of a language (a BCP 47 language tag, matched case-insensitively with the fallbacks of Chrome,
    /// e.g. <c>en</c> is en-us, <c>de-DE</c> is de-1996, <c>pt-BR</c> is pt), or null when the language is empty or has no
    /// dictionary.
    /// </summary>
    public static Hyphenator? Get(string? language)
    {
        if (string.IsNullOrEmpty(language))
            return null;

        // HyphenationMinikin::MapLocale: the mapped locale is mapped again until it does not change
        var locale = language;
        for (var i = 0; i < 4; i++)
        {
            var mapped = MapLocale(locale);
            if (string.Equals(mapped, locale, StringComparison.OrdinalIgnoreCase))
                break;

            locale = mapped;
        }

        return Registry.Indexes.TryGetValue(locale, out var index) ? Registry.Dictionaries[index].Value : null;
    }

    /// <summary>The locale of the dictionary of a locale: the fallback of the locale or of its prefixes, or its first subtag.</summary>
    private static string MapLocale(string locale)
    {
        var mapped = locale;
        while (true)
        {
            if (LocaleFallbacks.TryGetValue(mapped, out var fallback))
                return fallback.ExactMatch is not null && mapped.Length == locale.Length ? fallback.ExactMatch : fallback.Locale;

            var lastHyphen = mapped.LastIndexOf('-', StringComparison.Ordinal);
            if (lastHyphen <= 0)
                return mapped;

            mapped = mapped[..lastHyphen];
        }
    }

    /// <summary>
    /// Finds the automatic hyphenation opportunities of a word, as Chrome does (<c>Hyphenation::HyphenLocations</c>): sets
    /// <c>opportunities[i]</c> when the word may be hyphenated before <c>text[i]</c>, and returns whether there is one. The text
    /// is the content between two soft wrap opportunities: the leading spaces, opening punctuation and quotes and the trailing
    /// spaces, punctuation and controls are not part of the word.
    /// </summary>
    /// <param name="minWordLength">The minimum number of characters of the word, 0 for auto (<c>hyphenate-limit-chars</c>).</param>
    /// <param name="minPrefixLength">The minimum number of characters before the hyphen, 0 for auto.</param>
    /// <param name="minSuffixLength">The minimum number of characters after the hyphen, 0 for auto (the value before the hyphen when it is set).</param>
    public bool FindOpportunities(ReadOnlySpan<char> text, int minWordLength, int minPrefixLength, int minSuffixLength, Span<bool> opportunities)
    {
        // Hyphenation::SetLimits
        var prefix = minPrefixLength > 0 ? minPrefixLength : DefaultMinPrefixLength;
        var suffix = minSuffixLength > 0 ? minSuffixLength : minPrefixLength > 0 ? minPrefixLength : DefaultMinSuffixLength;
        var wordLength = Math.Max(minWordLength > 0 ? minWordLength : DefaultMinWordLength, prefix + suffix);

        // HyphenationMinikin::WordToHyphenate
        var start = 0;
        while (start < text.Length && Rune.DecodeFromUtf16(text[start..], out var rune, out var length) == OperationStatus.Done && IsLeadingCharacter(rune.Value))
        {
            start += length;
        }

        var end = text.Length;
        while (end > start && Rune.DecodeLastFromUtf16(text[start..end], out var rune, out var length) == OperationStatus.Done && IsTrailingCharacter(rune.Value))
        {
            end -= length;
        }

        // Hyphenation::ShouldHyphenateWord
        var word = text[start..end];
        if (word.Length < wordLength || word.Length > MaxWordLength)
            return false;

        if (!HyphenatesCapitalizedWords && Rune.DecodeFromUtf16(word, out var first, out _) == OperationStatus.Done && UnicodeProperties.GetGeneralCategory(first.Value) == UnicodeCategory.UppercaseLetter)
            return false;

        Span<bool> points = stackalloc bool[MaxWordLength];
        points = points[..word.Length];
        if (!Hyphenate(word, points))
            return false;

        var found = false;
        for (var i = prefix; i <= word.Length - suffix; i++)
        {
            if (points[i])
            {
                opportunities[start + i] = true;
                found = true;
            }
        }

        return found;

        // Character::TreatAsSpace, and the opening punctuation and the quotes (Line_Break OP and QU)
        static bool IsLeadingCharacter(int codePoint) => codePoint is ' ' or '\t' or '\n' or 0xA0 || UnicodeProperties.GetLineBreakClass(codePoint) is LineBreakClass.OP or LineBreakClass.QU;

        // The space separators, the punctuation and the controls
        static bool IsTrailingCharacter(int codePoint) => UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.SpaceSeparator or UnicodeCategory.Control
            or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
    }

    /// <summary>
    /// Liang's algorithm (Android's <c>Hyphenator::hyphenate</c>): sets <c>result[i]</c> when the patterns allow a hyphen before
    /// <c>word[i]</c>, at least 2 characters after the start of the word and 3 before its end. Returns false when the word
    /// cannot be hyphenated: too short or too long, or with a character that is not a letter of the patterns.
    /// </summary>
    internal bool Hyphenate(ReadOnlySpan<char> word, Span<bool> result)
    {
        result.Clear();
        if (word.Length < PatternMinPrefix + PatternMinSuffix || word.Length > MaxWordLength)
            return false;

        // The letters of the word between two word boundaries (0), lowercase (Hyphenator::alphabetLookup)
        Span<byte> letters = stackalloc byte[MaxWordLength + 2];
        letters = letters[..(word.Length + 2)];
        letters[0] = 0;
        letters[^1] = 0;
        for (var i = 0; i < word.Length; i++)
        {
            var index = _alphabet.AsSpan().BinarySearch((ushort)word[i]);
            if (index < 0)
                return false;

            letters[i + 1] = _letters[index];
        }

        // Hyphenator::hyphenateFromCodes: the values of the patterns that match at each position of the word, combined with
        // max; points[m] is the value before letters[m] (before word[m - 1])
        Span<byte> points = stackalloc byte[MaxWordLength + 3];
        points = points[..(letters.Length + 1)];
        points.Clear();
        for (var i = 0; i < letters.Length - 1; i++)
        {
            var node = 0;
            for (var j = i; j < letters.Length; j++)
            {
                node = FindChild(node, letters[j]);
                if (node < 0)
                    break;

                int offset = _valueOffsets[node];
                if (offset == 0)
                    continue;

                var position = i + _values[offset];
                var digits = _values.AsSpan(offset + 2, _values[offset + 1]);
                for (var k = 0; k < digits.Length; k++)
                {
                    if (digits[k] > points[position + k])
                    {
                        points[position + k] = digits[k];
                    }
                }
            }
        }

        // An odd value is a hyphenation opportunity
        for (var i = PatternMinPrefix; i <= word.Length - PatternMinSuffix; i++)
        {
            result[i] = (points[i + 1] & 1) != 0;
        }

        return true;
    }

    /// <summary>Returns the child of a node of the trie for a letter, or -1.</summary>
    private int FindChild(int node, byte letter)
    {
        var first = _firstChildren[node];
        var end = first + _childCounts[node];
        for (var child = first; child < end; child++)
        {
            var edge = _edgeLetters[child];
            if (edge == letter)
                return child;

            if (edge > letter)
                break;
        }

        return -1;
    }

    private static Hyphenator Load(string locale, string resource)
    {
        using var stream = typeof(Hyphenator).Assembly.GetManifestResourceStream(ResourcePrefix + resource + ".hyph") ?? throw new InvalidOperationException($"The hyphenation dictionary '{resource}' is missing");
        var compressed = new byte[stream.Length];
        stream.ReadExactly(compressed);

        // A 32-bit length, then the Brotli stream (GeneratorContext.WriteCompressedFile of tools/Meziantou.Framework.WebPlatform.SpecGen)
        var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(compressed)];
        if (!BrotliDecoder.TryDecompress(compressed.AsSpan(4), data, out var written) || written != data.Length)
            throw new InvalidDataException($"The hyphenation dictionary '{resource}' is invalid");

        return new Hyphenator(locale, data);
    }

    /// <summary>The dictionaries, loaded on first use (in a nested class: the tables of the generated part are initialized first).</summary>
    private static class Registry
    {
        public static readonly FrozenDictionary<string, int> Indexes = DictionaryNames.Index().ToFrozenDictionary(entry => entry.Item, entry => entry.Index, StringComparer.OrdinalIgnoreCase);

        public static readonly Lazy<Hyphenator>[] Dictionaries = [.. DictionaryNames.Select((name, index) => new Lazy<Hyphenator>(() => Load(name, DictionaryResources[index]), LazyThreadSafetyMode.ExecutionAndPublication))];
    }
}
