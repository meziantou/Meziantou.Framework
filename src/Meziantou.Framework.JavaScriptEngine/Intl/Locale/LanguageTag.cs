namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// A Unicode BCP 47 locale identifier (UTS 35 unicode_locale_id) as ECMA-402 accepts it (IsStructurallyValidLanguageTag:
/// "-" separators only, no duplicate variant or singleton, no extlang, no grandfathered tag). The subtags are stored in
/// canonical case. <see cref="Canonicalize"/> applies the canonicalization of UTS 35 (aliases, order of the variants,
/// of the extensions and of the keywords).
/// </summary>
internal sealed class LanguageTag
{
    public string Language { get; set; } = "";

    /// <summary>Gets or sets the script subtag (title case), or an empty string.</summary>
    public string Script { get; set; } = "";

    /// <summary>Gets or sets the region subtag (upper case), or an empty string.</summary>
    public string Region { get; set; } = "";

    /// <summary>Gets the variant subtags (lower case).</summary>
    public List<string> Variants { get; } = [];

    /// <summary>Gets or sets whether the identifier has a Unicode locale extension (-u-).</summary>
    public bool HasUnicodeExtension { get; set; }

    /// <summary>Gets the attributes of the Unicode locale extension.</summary>
    public List<string> Attributes { get; } = [];

    /// <summary>Gets the keywords of the Unicode locale extension; a keyword without a type has the value "".</summary>
    public List<KeyValuePair<string, string>> Keywords { get; } = [];

    /// <summary>Gets or sets the tlang of the transformed extension (-t-), with its subtags in lower case, or null.</summary>
    public LanguageTag? TransformedLanguage { get; set; }

    /// <summary>Gets or sets whether the identifier has a transformed extension (-t-).</summary>
    public bool HasTransformedExtension { get; set; }

    /// <summary>Gets the fields of the transformed extension (tkey, tvalue).</summary>
    public List<KeyValuePair<string, string>> TransformedFields { get; } = [];

    /// <summary>Gets the other extensions: the singleton and the subtags after it ("a", "bar-baz").</summary>
    public List<KeyValuePair<char, string>> OtherExtensions { get; } = [];

    /// <summary>Gets or sets the subtags of the private use extension, after "x-", or null.</summary>
    public string? PrivateUse { get; set; }

    /// <summary>Parses a language tag; returns null when it is not structurally valid.</summary>
    public static LanguageTag? Parse(string tag)
    {
        if (tag.Length == 0)
            return null;

        foreach (var c in tag)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                return null;
        }

        var subtags = tag.ToLowerInvariant().Split('-');
        foreach (var subtag in subtags)
        {
            if (subtag.Length == 0 || subtag.Length > 8)
                return null;
        }

        var result = new LanguageTag();
        var index = 0;
        if (!ParseLanguageId(subtags, ref index, result, transformed: false))
            return null;

        Span<bool> singletons = stackalloc bool[36];
        while (index < subtags.Length)
        {
            var subtag = subtags[index];
            if (subtag.Length != 1)
                return null;

            var singleton = subtag[0];
            index++;
            if (singleton == 'x')
            {
                var start = index;
                while (index < subtags.Length)
                {
                    index++;
                }

                if (start == index)
                    return null;

                result.PrivateUse = string.Join('-', subtags, start, index - start);
                break;
            }

            var singletonIndex = char.IsAsciiDigit(singleton) ? singleton - '0' : singleton - 'a' + 10;
            if (singletons[singletonIndex])
                return null;

            singletons[singletonIndex] = true;
            switch (singleton)
            {
                case 'u':
                    if (!ParseUnicodeExtension(subtags, ref index, result))
                        return null;

                    break;

                case 't':
                    if (!ParseTransformedExtension(subtags, ref index, result))
                        return null;

                    break;

                default:
                    var start = index;
                    while (index < subtags.Length && subtags[index].Length >= 2)
                    {
                        index++;
                    }

                    if (start == index)
                        return null;

                    result.OtherExtensions.Add(new(singleton, string.Join('-', subtags, start, index - start)));
                    break;
            }
        }

        return result;
    }

    private static bool ParseLanguageId(string[] subtags, ref int index, LanguageTag result, bool transformed)
    {
        if (index >= subtags.Length || !IsLanguageSubtag(subtags[index]))
            return false;

        result.Language = subtags[index++];
        if (index < subtags.Length && IsScriptSubtag(subtags[index]))
        {
            result.Script = transformed ? subtags[index] : ToTitle(subtags[index]);
            index++;
        }

        if (index < subtags.Length && IsRegionSubtag(subtags[index]))
        {
            result.Region = transformed ? subtags[index] : subtags[index].ToUpperInvariant();
            index++;
        }

        while (index < subtags.Length && IsVariantSubtag(subtags[index]))
        {
            if (result.Variants.Contains(subtags[index]))
                return false;

            result.Variants.Add(subtags[index]);
            index++;
        }

        // The next subtag must be a singleton
        return index >= subtags.Length || subtags[index].Length == 1;
    }

    private static bool ParseUnicodeExtension(string[] subtags, ref int index, LanguageTag result)
    {
        var start = index;
        result.HasUnicodeExtension = true;
        while (index < subtags.Length && subtags[index].Length is >= 3 and <= 8)
        {
            result.Attributes.Add(subtags[index]);
            index++;
        }

        while (index < subtags.Length && subtags[index].Length == 2)
        {
            var key = subtags[index];
            if (!char.IsAsciiLetter(key[1]))
                return false;

            index++;
            var typeStart = index;
            while (index < subtags.Length && subtags[index].Length is >= 3 and <= 8)
            {
                index++;
            }

            result.Keywords.Add(new(key, string.Join('-', subtags, typeStart, index - typeStart)));
        }

        return index > start && (index >= subtags.Length || subtags[index].Length == 1);
    }

    private static bool ParseTransformedExtension(string[] subtags, ref int index, LanguageTag result)
    {
        var start = index;
        result.HasTransformedExtension = true;
        if (index < subtags.Length && IsLanguageSubtag(subtags[index]))
        {
            var tlang = new LanguageTag();
            var tlangIndex = index;
            if (!ParseTransformedLanguage(subtags, ref tlangIndex, tlang))
                return false;

            index = tlangIndex;
            result.TransformedLanguage = tlang;
        }

        while (index < subtags.Length && subtags[index].Length == 2)
        {
            var key = subtags[index];
            if (!char.IsAsciiLetter(key[0]) || !char.IsAsciiDigit(key[1]))
                return false;

            index++;
            var valueStart = index;
            while (index < subtags.Length && subtags[index].Length is >= 3 and <= 8)
            {
                index++;
            }

            if (valueStart == index)
                return false;

            result.TransformedFields.Add(new(key, string.Join('-', subtags, valueStart, index - valueStart)));
        }

        return index > start && (index >= subtags.Length || subtags[index].Length == 1);
    }

    // tlang: the subtags of the language id, followed by a tfield or a singleton
    private static bool ParseTransformedLanguage(string[] subtags, ref int index, LanguageTag result)
    {
        result.Language = subtags[index++];
        if (index < subtags.Length && IsScriptSubtag(subtags[index]))
        {
            result.Script = subtags[index++];
        }

        if (index < subtags.Length && IsRegionSubtag(subtags[index]))
        {
            result.Region = subtags[index++];
        }

        while (index < subtags.Length && IsVariantSubtag(subtags[index]))
        {
            if (result.Variants.Contains(subtags[index]))
                return false;

            result.Variants.Add(subtags[index]);
            index++;
        }

        return index >= subtags.Length || subtags[index].Length == 1 || IsTransformedKey(subtags[index]);
    }

    private static bool IsTransformedKey(string value) => value.Length == 2 && char.IsAsciiLetter(value[0]) && char.IsAsciiDigit(value[1]);

    /// <summary>unicode_language_subtag: alpha{2,3} | alpha{5,8}</summary>
    public static bool IsLanguageSubtag(ReadOnlySpan<char> value) => value.Length is 2 or 3 or (>= 5 and <= 8) && IsAlpha(value);

    /// <summary>unicode_script_subtag: alpha{4}</summary>
    public static bool IsScriptSubtag(ReadOnlySpan<char> value) => value.Length == 4 && IsAlpha(value);

    /// <summary>unicode_region_subtag: alpha{2} | digit{3}</summary>
    public static bool IsRegionSubtag(ReadOnlySpan<char> value) => (value.Length == 2 && IsAlpha(value)) || (value.Length == 3 && IsDigit(value));

    /// <summary>unicode_variant_subtag: alphanum{5,8} | digit alphanum{3}</summary>
    public static bool IsVariantSubtag(ReadOnlySpan<char> value) => (value.Length is >= 5 and <= 8 && IsAlphaNumeric(value)) || (value.Length == 4 && char.IsAsciiDigit(value[0]) && IsAlphaNumeric(value));

    /// <summary>type (of a Unicode locale extension keyword): alphanum{3,8} (sep alphanum{3,8})*</summary>
    public static bool IsUnicodeType(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '-')
            {
                var length = i - start;
                if (length is < 3 or > 8)
                    return false;

                start = i + 1;
            }
            else if (!char.IsAsciiLetterOrDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAlpha(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetter(c))
                return false;
        }

        return true;
    }

    private static bool IsDigit(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    private static bool IsAlphaNumeric(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }

        return true;
    }

    internal static string ToTitle(string value) => value.Length == 0 ? value : string.Concat(value[..1].ToUpperInvariant(), value[1..].ToLowerInvariant());

    /// <summary>Gets the language id (language, script, region and variants), without the extensions.</summary>
    public string BaseName
    {
        get
        {
            var builder = new StringBuilder();
            AppendLanguageId(builder, this);
            return builder.ToString();
        }
    }

    /// <summary>Gets the value of a keyword of the Unicode locale extension, or null when it is absent.</summary>
    public string? GetKeyword(string key)
    {
        foreach (var keyword in Keywords)
        {
            if (keyword.Key == key)
                return keyword.Value;
        }

        return null;
    }

    /// <summary>Sets the value of a keyword of the Unicode locale extension (adds the extension when needed).</summary>
    public void SetKeyword(string key, string value)
    {
        HasUnicodeExtension = true;
        for (var i = 0; i < Keywords.Count; i++)
        {
            if (Keywords[i].Key == key)
            {
                Keywords[i] = new(key, value);
                return;
            }
        }

        Keywords.Add(new(key, value));
    }

    /// <summary>Removes the Unicode locale extension.</summary>
    public void RemoveUnicodeExtension()
    {
        HasUnicodeExtension = false;
        Attributes.Clear();
        Keywords.Clear();
    }

    public LanguageTag Clone()
    {
        var result = new LanguageTag
        {
            Language = Language,
            Script = Script,
            Region = Region,
            HasUnicodeExtension = HasUnicodeExtension,
            TransformedLanguage = TransformedLanguage?.Clone(),
            HasTransformedExtension = HasTransformedExtension,
            PrivateUse = PrivateUse,
        };
        result.Variants.AddRange(Variants);
        result.Attributes.AddRange(Attributes);
        result.Keywords.AddRange(Keywords);
        result.TransformedFields.AddRange(TransformedFields);
        result.OtherExtensions.AddRange(OtherExtensions);
        return result;
    }

    /// <summary>
    /// Applies the canonicalization of UTS 35 (CanonicalizeUnicodeLocaleId): the aliases of the language id and of the
    /// tlang, the values of the keywords and of the tfields, the order of the variants, extensions, keywords and tfields,
    /// and the removal of the "true" values of the keywords.
    /// </summary>
    public void Canonicalize()
    {
        LocaleCanonicalizer.CanonicalizeLanguageId(this, transformed: false);

        if (HasUnicodeExtension)
        {
            // Duplicate attributes and keywords: the first one is kept
            var attributes = new List<string>(Attributes.Count);
            foreach (var attribute in Attributes)
            {
                if (!attributes.Contains(attribute))
                {
                    attributes.Add(attribute);
                }
            }

            attributes.Sort(StringComparer.Ordinal);
            Attributes.Clear();
            Attributes.AddRange(attributes);

            var keywords = new List<KeyValuePair<string, string>>(Keywords.Count);
            foreach (var keyword in Keywords)
            {
                var exists = false;
                foreach (var existing in keywords)
                {
                    if (existing.Key == keyword.Key)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    var value = LocaleCanonicalizer.CanonicalizeUnicodeValue(keyword.Key, keyword.Value);
                    keywords.Add(new(keyword.Key, value));
                }
            }

            keywords.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
            Keywords.Clear();
            Keywords.AddRange(keywords);
        }

        if (HasTransformedExtension)
        {
            if (TransformedLanguage is not null)
            {
                LocaleCanonicalizer.CanonicalizeLanguageId(TransformedLanguage, transformed: true);
            }

            var fields = new List<KeyValuePair<string, string>>(TransformedFields.Count);
            foreach (var field in TransformedFields)
            {
                fields.Add(new(field.Key, LocaleCanonicalizer.CanonicalizeTransformedValue(field.Key, field.Value)));
            }

            fields.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
            TransformedFields.Clear();
            TransformedFields.AddRange(fields);
        }

        OtherExtensions.Sort(static (a, b) => a.Key.CompareTo(b.Key));
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        AppendLanguageId(builder, this);

        // The extensions in the order of their singleton: the digits, then the letters
        var unicodeWritten = !HasUnicodeExtension;
        var transformedWritten = !HasTransformedExtension;
        foreach (var extension in OtherExtensions)
        {
            if (!transformedWritten && extension.Key > 't')
            {
                AppendTransformed(builder);
                transformedWritten = true;
            }

            if (!unicodeWritten && extension.Key > 'u')
            {
                AppendUnicode(builder);
                unicodeWritten = true;
            }

            builder.Append('-').Append(extension.Key).Append('-').Append(extension.Value);
        }

        if (!transformedWritten)
        {
            AppendTransformed(builder);
        }

        if (!unicodeWritten)
        {
            AppendUnicode(builder);
        }

        if (PrivateUse is not null)
        {
            builder.Append("-x-").Append(PrivateUse);
        }

        return builder.ToString();
    }

    private void AppendUnicode(StringBuilder builder)
    {
        builder.Append("-u");
        foreach (var attribute in Attributes)
        {
            builder.Append('-').Append(attribute);
        }

        foreach (var keyword in Keywords)
        {
            builder.Append('-').Append(keyword.Key);
            if (keyword.Value.Length > 0)
            {
                builder.Append('-').Append(keyword.Value);
            }
        }
    }

    private void AppendTransformed(StringBuilder builder)
    {
        builder.Append("-t");
        if (TransformedLanguage is not null)
        {
            builder.Append('-');
            AppendLanguageId(builder, TransformedLanguage);
        }

        foreach (var field in TransformedFields)
        {
            builder.Append('-').Append(field.Key).Append('-').Append(field.Value);
        }
    }

    private static void AppendLanguageId(StringBuilder builder, LanguageTag tag)
    {
        builder.Append(tag.Language);
        if (tag.Script.Length > 0)
        {
            builder.Append('-').Append(tag.Script);
        }

        if (tag.Region.Length > 0)
        {
            builder.Append('-').Append(tag.Region);
        }

        foreach (var variant in tag.Variants)
        {
            builder.Append('-').Append(variant);
        }
    }
}
