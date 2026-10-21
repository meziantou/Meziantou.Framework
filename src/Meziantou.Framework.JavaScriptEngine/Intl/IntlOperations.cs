using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The data of the locales for a service (ResolveLocale's localeData): the supported values of a key in a locale; the first one is the default.</summary>
internal delegate IReadOnlyList<string?> LocaleKeyData(string locale, string key);

/// <summary>The result of ResolveLocale.</summary>
internal sealed class ResolvedLocale
{
    /// <summary>Gets the locale, with the supported Unicode extension keywords (result.[[Locale]]).</summary>
    public required string Locale { get; init; }

    /// <summary>Gets the available locale whose data is used (the locale without the extension keywords, result.[[DataLocale]]).</summary>
    public required string DataLocale { get; init; }

    /// <summary>Gets the values of the relevant extension keys; null when the locale data has no value.</summary>
    public required Dictionary<string, string?> Values { get; init; }

    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>The abstract operations of ECMA-402 (https://tc39.es/ecma402/#sec-abstract-operations).</summary>
internal static class IntlOperations
{
    private static readonly PropertyKey LengthKey = new("length");

    public static CldrData Data => CldrData.Default;

    /// <summary>Gets whether a canonical locale is in [[AvailableLocales]] (the locales of the CLDR data).</summary>
    public static bool IsAvailableLocale(string locale) => Data.IsAvailableLocale(locale);

    /// <summary>
    /// DefaultLocale(): the locale of <see cref="JsEngineOptions.DefaultLocale"/>, canonicalized, when it matches an
    /// available locale; else "en-US".
    /// </summary>
    public static string DefaultLocale(JsEngine engine)
    {
        var configured = engine.Options.DefaultLocale;
        if (configured is not null && LanguageTag.Parse(configured) is { } tag)
        {
            tag.Canonicalize();
            tag.RemoveUnicodeExtension();
            var canonical = tag.ToString();
            if (LookupMatchingLocaleByPrefix(canonical) is { } match)
                return match;
        }

        return "en-US";
    }

    /// <summary>IsStructurallyValidLanguageTag(locale) then CanonicalizeUnicodeLocaleId(locale); null when the tag is not valid.</summary>
    public static string? CanonicalizeLanguageTag(string tag)
    {
        var parsed = LanguageTag.Parse(tag);
        if (parsed is null)
            return null;

        parsed.Canonicalize();
        return parsed.ToString();
    }

    /// <summary>CanonicalizeLocaleList(locales)</summary>
    public static List<string> CanonicalizeLocaleList(JsValue locales)
    {
        var seen = new List<string>();
        if (locales.IsUndefined)
            return seen;

        JsObject o;
        if (locales.IsString || locales.Cell is JsLocaleObject)
        {
            o = JsArray.Create(JsEngine.Current!.CurrentRealm!, [locales]);
        }
        else
        {
            o = JsOperations.ToObject(locales);
        }

        var length = JsOperations.ToLength(o.Get(LengthKey));
        for (double k = 0; k < length; k++)
        {
            var pk = JsOperations.ToPropertyKey(k);
            if (!o.HasProperty(pk))
                continue;

            var kValue = o.Get(pk);
            if (!kValue.IsString && !kValue.IsObject)
                ThrowHelper.ThrowTypeError("The locale must be a string or an object");

            string tag;
            if (kValue.Cell is JsLocaleObject localeObject)
            {
                tag = localeObject.Locale;
            }
            else
            {
                tag = JsOperations.ToString(kValue);
            }

            var canonicalized = CanonicalizeLanguageTag(tag) ?? ThrowHelper.ThrowRangeError<string>("Incorrect locale information provided: " + tag);
            if (!seen.Contains(canonicalized))
            {
                seen.Add(canonicalized);
            }
        }

        return seen;
    }

    /// <summary>Removes the Unicode locale extension sequence of a canonical locale.</summary>
    public static string RemoveUnicodeExtension(string locale, out string? extension)
    {
        extension = null;
        var index = FindUnicodeExtension(locale, out var end);
        if (index < 0)
            return locale;

        extension = locale[index..end];
        return string.Concat(locale.AsSpan(0, index), locale.AsSpan(end));
    }

    // The index of "-u-" (outside the private use extension) and the end of the extension
    private static int FindUnicodeExtension(string locale, out int end)
    {
        end = -1;
        var privateUse = locale.StartsWith("x-", StringComparison.Ordinal) ? 0 : locale.IndexOf("-x-", StringComparison.Ordinal);
        var index = locale.IndexOf("-u-", StringComparison.Ordinal);
        if (index < 0 || (privateUse >= 0 && index > privateUse))
            return -1;

        // The extension ends at the next singleton
        end = locale.Length;
        var position = index + 3;
        while (position < locale.Length)
        {
            var next = locale.IndexOf('-', position, StringComparison.Ordinal);
            if (next < 0)
                break;

            if (next + 2 == locale.Length || (next + 2 < locale.Length && locale[next + 2] == '-'))
            {
                end = next;
                break;
            }

            position = next + 1;
        }

        return index;
    }

    /// <summary>LookupMatchingLocaleByPrefix for one locale without its Unicode extension: the longest available prefix, or null.</summary>
    public static string? LookupMatchingLocaleByPrefix(string locale, Func<string, bool>? isAvailable = null)
    {
        var prefix = locale;
        while (prefix.Length > 0)
        {
            if (isAvailable is null ? IsAvailableLocale(prefix) : isAvailable(prefix))
                return prefix;

            var pos = prefix.LastIndexOf('-', StringComparison.Ordinal);
            if (pos < 0)
            {
                pos = 0;
            }

            while (pos >= 2 && prefix[pos - 2] == '-')
            {
                pos -= 2;
            }

            prefix = prefix[..pos];
        }

        return null;
    }

    /// <summary>LookupMatchingLocaleByPrefix / LookupMatchingLocaleByBestFit (the same algorithm): the matched available locale and the Unicode extension of the request.</summary>
    public static (string Locale, string? Extension)? LookupMatchingLocale(List<string> requestedLocales, Func<string, bool>? isAvailable = null)
    {
        foreach (var requested in requestedLocales)
        {
            var locale = RemoveUnicodeExtension(requested, out var extension);
            if (LookupMatchingLocaleByPrefix(locale, isAvailable) is { } match)
                return (match, extension);
        }

        return null;
    }

    /// <summary>
    /// ResolveLocale(availableLocales, requestedLocales, options, relevantExtensionKeys, localeData). The options are the
    /// values of the relevant keys read from the options object (null when undefined).
    /// </summary>
    public static ResolvedLocale ResolveLocale(List<string> requestedLocales, ReadOnlySpan<string> relevantExtensionKeys, Func<string, string?> options, LocaleKeyData localeData, Func<string, bool>? isAvailable = null)
    {
        var r = LookupMatchingLocale(requestedLocales, isAvailable) ?? (DefaultLocale(JsEngine.Current!), null);
        var foundLocale = r.Locale;
        var keywords = new List<KeyValuePair<string, string>>();
        if (r.Extension is not null && LanguageTag.Parse("und" + r.Extension) is { } extensionTag)
        {
            keywords = extensionTag.Keywords;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var supportedKeywords = new List<KeyValuePair<string, string>>();
        foreach (var key in relevantExtensionKeys)
        {
            var keyLocaleData = localeData(foundLocale, key);
            var value = keyLocaleData.Count > 0 ? keyLocaleData[0] : null;
            KeyValuePair<string, string>? supportedKeyword = null;
            foreach (var entry in keywords)
            {
                if (entry.Key != key)
                    continue;

                var requestedValue = entry.Value;
                if (requestedValue.Length > 0)
                {
                    if (Contains(keyLocaleData, requestedValue))
                    {
                        value = requestedValue;
                        supportedKeyword = new(key, value);
                    }
                }
                else if (Contains(keyLocaleData, "true"))
                {
                    value = "true";
                    supportedKeyword = new(key, "");
                }

                break;
            }

            var optionsValue = options(key);
            if (optionsValue is not null)
            {
                optionsValue = LocaleCanonicalizer.CanonicalizeUnicodeValue(key, optionsValue.ToLowerInvariant());
                if (optionsValue.Length == 0)
                {
                    optionsValue = "true";
                }
            }

            if (optionsValue is not null && optionsValue != value && Contains(keyLocaleData, optionsValue))
            {
                value = optionsValue;
                supportedKeyword = null;
            }

            if (supportedKeyword is { } keyword)
            {
                supportedKeywords.Add(keyword);
            }

            values[key] = value;
        }

        var resultLocale = foundLocale;
        if (supportedKeywords.Count > 0)
        {
            var tag = LanguageTag.Parse(foundLocale)!;
            foreach (var keyword in supportedKeywords)
            {
                tag.SetKeyword(keyword.Key, keyword.Value);
            }

            tag.Canonicalize();
            resultLocale = tag.ToString();
        }

        return new ResolvedLocale { Locale = resultLocale, DataLocale = foundLocale, Values = values };

        static bool Contains(IReadOnlyList<string?> list, string value)
        {
            foreach (var item in list)
            {
                if (item == value)
                    return true;
            }

            return false;
        }
    }

    /// <summary>FilterLocales / SupportedLocales(availableLocales, requestedLocales, options)</summary>
    public static JsArray SupportedLocales(JsRealm realm, List<string> requestedLocales, JsValue options, Func<string, bool>? isAvailable = null)
    {
        var optionsObject = CoerceOptionsToObject(options);
        _ = GetStringOption(optionsObject, "localeMatcher", ["lookup", "best fit"], "best fit");
        var subset = new List<JsValue>();
        foreach (var locale in requestedLocales)
        {
            var noExtensionsLocale = RemoveUnicodeExtension(locale, out _);
            if (LookupMatchingLocaleByPrefix(noExtensionsLocale, isAvailable) is not null)
            {
                subset.Add(locale);
            }
        }

        return JsArray.Create(realm, [.. subset]);
    }

    /// <summary>GetOptionsObject(options)</summary>
    public static JsObject GetOptionsObject(JsValue options)
    {
        if (options.IsUndefined)
            return new JsObject(prototype: null);

        if (options.TryGetObject() is { } obj)
            return obj;

        return ThrowHelper.ThrowTypeError<JsObject>("The options must be an object");
    }

    /// <summary>CoerceOptionsToObject(options)</summary>
    public static JsObject CoerceOptionsToObject(JsValue options)
    {
        if (options.IsUndefined)
            return new JsObject(prototype: null);

        return JsOperations.ToObject(options);
    }

    /// <summary>GetOption(options, property, string, values, default)</summary>
    public static string? GetStringOption(JsObject options, string property, ReadOnlySpan<string> values, string? fallback)
    {
        var value = options.Get(property);
        if (value.IsUndefined)
            return fallback;

        var text = JsOperations.ToString(value);
        if (values.Length > 0 && !values.Contains(text))
            ThrowHelper.ThrowRangeError($"Value {text} out of range for option {property}");

        return text;
    }

    /// <summary>GetOption(options, property, boolean, empty, default)</summary>
    public static bool? GetBooleanOption(JsObject options, string property, bool? fallback)
    {
        var value = options.Get(property);
        if (value.IsUndefined)
            return fallback;

        return JsOperations.ToBoolean(value);
    }

    /// <summary>GetNumberOption(options, property, minimum, maximum, fallback)</summary>
    public static int? GetNumberOption(JsObject options, string property, int minimum, int maximum, int? fallback)
    {
        var value = options.Get(property);
        return DefaultNumberOption(value, minimum, maximum, fallback, property);
    }

    /// <summary>DefaultNumberOption(value, minimum, maximum, fallback)</summary>
    public static int? DefaultNumberOption(JsValue value, int minimum, int maximum, int? fallback, string property)
    {
        if (value.IsUndefined)
            return fallback;

        var number = JsOperations.ToNumber(value);
        if (double.IsNaN(number) || number < minimum || number > maximum)
            ThrowHelper.ThrowRangeError($"{property} value is out of range.");

        return (int)Math.Floor(number);
    }

    /// <summary>Creates an array of strings.</summary>
    public static JsArray CreateArray(JsRealm realm, IReadOnlyList<string> values)
    {
        var items = new JsValue[values.Count];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = values[i];
        }

        return JsArray.Create(realm, items);
    }
}
