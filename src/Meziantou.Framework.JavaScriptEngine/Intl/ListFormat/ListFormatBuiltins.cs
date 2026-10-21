using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.ListFormat instance: an ordinary object with the [[InitializedListFormat]] internal slot.</summary>
internal sealed class JsListFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    /// <summary>[[Type]]: "conjunction", "disjunction" or "unit".</summary>
    public string Type { get; set; } = "conjunction";

    /// <summary>[[Style]]: "long", "short" or "narrow".</summary>
    public string Style { get; set; } = "long";

    /// <summary>[[Templates]]</summary>
    public ListPatterns Templates { get; set; } = null!;
}

/// <summary>A part of a formatted list.</summary>
internal readonly record struct ListPart(string Type, string Value);

/// <summary>
/// The list patterns of a locale, type and style (CLDR listPatterns), with the contextual variants of the patterns
/// of the last two elements that ICU applies (Spanish "y" / "e" and "o" / "u", Hebrew "\u05D5" / "\u05D5-").
/// </summary>
internal sealed class ListPatterns
{
    private const string SpanishY = "{0} y {1}";
    private const string SpanishE = "{0} e {1}";
    private const string SpanishO = "{0} o {1}";
    private const string SpanishU = "{0} u {1}";
    private const string HebrewVav = "{0} \u05D5{1}";
    private const string HebrewVavDash = "{0} \u05D5-{1}";

    private readonly string _two;
    private readonly string _end;
    private readonly string? _contextualTwo;
    private readonly string? _contextualEnd;
    private readonly Func<string, bool>? _useContextual;

    public ListPatterns(CldrLocale locale, string type, string style)
    {
        var name = "listPatterns/listPattern-type-" + type switch
        {
            "disjunction" => "or",
            "unit" => "unit",
            _ => "standard",
        } + (style is "long" ? "" : "-" + style) + "/";
        Start = locale.GetString(CldrCategory.Misc, name + "start") ?? "{0}, {1}";
        Middle = locale.GetString(CldrCategory.Misc, name + "middle") ?? "{0}, {1}";
        _end = locale.GetString(CldrCategory.Misc, name + "end") ?? "{0}, {1}";
        _two = locale.GetString(CldrCategory.Misc, name + "2") ?? "{0}, {1}";

        switch (locale.Language)
        {
            case "es":
                if (_two is SpanishY || _end is SpanishY)
                {
                    _useContextual = ShouldChangeToE;
                    _contextualTwo = _two is SpanishY ? SpanishE : _two;
                    _contextualEnd = _end is SpanishY ? SpanishE : _end;
                }
                else if (_two is SpanishO || _end is SpanishO)
                {
                    _useContextual = ShouldChangeToU;
                    _contextualTwo = _two is SpanishO ? SpanishU : _two;
                    _contextualEnd = _end is SpanishO ? SpanishU : _end;
                }

                break;

            case "he":
                if (_two is HebrewVav || _end is HebrewVav)
                {
                    _useContextual = ShouldChangeToVavDash;
                    _contextualTwo = _two is HebrewVav ? HebrewVavDash : _two;
                    _contextualEnd = _end is HebrewVav ? HebrewVavDash : _end;
                }

                break;
        }
    }

    public string Start { get; }

    public string Middle { get; }

    /// <summary>Gets the pattern of a list of two elements, for the second element.</summary>
    public string GetTwo(string next) => _useContextual is not null && _useContextual(next) ? _contextualTwo! : _two;

    /// <summary>Gets the pattern of the last two elements of a list of three or more elements, for the last element.</summary>
    public string GetEnd(string next) => _useContextual is not null && _useContextual(next) ? _contextualEnd! : _end;

    // "y" becomes "e" before a word that starts with "i" or "hi", but not "hia" or "hie" (as ICU)
    private static bool ShouldChangeToE(string text)
    {
        if (text.Length == 0)
            return false;

        if (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'i' or 'I' && (text.Length == 2 || text[2] is not ('a' or 'A' or 'e' or 'E')))
            return true;

        return text[0] is 'i' or 'I';
    }

    // "o" becomes "u" before a word that starts with "o", "ho" or "8", and before "11" alone or followed by a space (as ICU)
    private static bool ShouldChangeToU(string text)
    {
        if (text.Length == 0)
            return false;

        if (text[0] is 'o' or 'O' or '8')
            return true;

        if (text[0] is 'h' or 'H' && text.Length > 1 && text[1] is 'o' or 'O')
            return true;

        return text.Length >= 2 && text[0] == '1' && text[1] == '1' && (text.Length == 2 || text[2] == ' ');
    }

    // "\u05D5" becomes "\u05D5-" before a word that does not start with a Hebrew character (as ICU)
    private static bool ShouldChangeToVavDash(string text)
    {
        if (text.Length == 0)
            return false;

        var codePoint = char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]) ? char.ConvertToUtf32(text[0], text[1]) : text[0];
        return UnicodeScripts.GetScript(codePoint) is not UnicodeScript.Hebrew;
    }
}

/// <summary>Intl.ListFormat (https://tc39.es/ecma402/#listformat-objects).</summary>
internal static class ListFormatBuiltins
{
    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("ListFormat", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.ListFormat", PropertyFlags.Configurable);
        realm.DefineMethod(prototype, "format", 1, Format);
        realm.DefineMethod(prototype, "formatToParts", 1, FormatToParts);
        realm.DefineMethod(prototype, "resolvedOptions", 0, ResolvedOptions);
        intl.DefineBuiltIn("ListFormat", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.ListFormat([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.ListFormat requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlListFormatPrototype);
        var listFormat = new JsListFormatObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var options = IntlOperations.GetOptionsObject(arguments.Length > 1 ? arguments[1] : default);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var r = IntlOperations.ResolveLocale(requestedLocales, [], static _ => null, static (_, _) => []);
        listFormat.Locale = r.Locale;
        listFormat.Type = IntlOperations.GetStringOption(options, "type", ["conjunction", "disjunction", "unit"], "conjunction")!;
        listFormat.Style = IntlOperations.GetStringOption(options, "style", ["long", "short", "narrow"], "long")!;
        listFormat.Templates = new ListPatterns(IntlOperations.Data.FindLocale(r.DataLocale), listFormat.Type, listFormat.Style);
        return listFormat;
    }

    private static JsListFormatObject ThisListFormat(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsListFormatObject listFormat)
            return listFormat;

        return ThrowHelper.ThrowTypeError<JsListFormatObject>($"Intl.ListFormat.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>Intl.ListFormat.prototype.format(list)</summary>
    private static JsValue Format(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var listFormat = ThisListFormat(thisValue, "format");
        var list = StringListFromIterable(arguments.Length > 0 ? arguments[0] : default);
        var builder = new StringBuilder();
        foreach (var part in CreatePartsFromList(listFormat.Templates, list))
        {
            builder.Append(part.Value);
        }

        return builder.ToString();
    }

    /// <summary>Intl.ListFormat.prototype.formatToParts(list)</summary>
    private static JsValue FormatToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var listFormat = ThisListFormat(thisValue, "formatToParts");
        var list = StringListFromIterable(arguments.Length > 0 ? arguments[0] : default);
        var parts = CreatePartsFromList(listFormat.Templates, list);
        var realm = function.Realm;
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Value);
            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    /// <summary>Intl.ListFormat.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var listFormat = ThisListFormat(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", listFormat.Locale);
        options.CreateDataPropertyOrThrow("type", listFormat.Type);
        options.CreateDataPropertyOrThrow("style", listFormat.Style);
        return options;
    }

    /// <summary>StringListFromIterable(iterable)</summary>
    private static List<string> StringListFromIterable(JsValue iterable)
    {
        var list = new List<string>();
        if (iterable.IsUndefined)
            return list;

        var iteratorRecord = IteratorOperations.GetIterator(iterable);
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            if (!next.IsString)
            {
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                ThrowHelper.ThrowTypeError("Iterable yielded " + JsOperations.TypeOf(next) + " which is not a string");
            }

            list.Add(next.AsString());
        }

        return list;
    }

    /// <summary>CreatePartsFromList(listFormat, list)</summary>
    internal static List<ListPart> CreatePartsFromList(ListPatterns templates, IReadOnlyList<string> list)
    {
        var parts = new List<ListPart>();
        var size = list.Count;
        if (size == 0)
            return parts;

        if (size == 1)
        {
            parts.Add(new ListPart("element", list[0]));
            return parts;
        }

        if (size == 2)
        {
            DeconstructPattern(parts, templates.GetTwo(list[1]), [new ListPart("element", list[0])], [new ListPart("element", list[1])]);
            return parts;
        }

        List<ListPart> tail = [new ListPart("element", list[size - 1])];
        for (var i = size - 2; i >= 0; i--)
        {
            var pattern = i == 0 ? templates.Start : i < size - 2 ? templates.Middle : templates.GetEnd(list[size - 1]);
            var result = new List<ListPart>();
            DeconstructPattern(result, pattern, [new ListPart("element", list[i])], tail);
            tail = result;
        }

        return tail;
    }

    /// <summary>DeconstructPattern(pattern, placeables) for the placeables {0} and {1}.</summary>
    private static void DeconstructPattern(List<ListPart> result, string pattern, List<ListPart> first, List<ListPart> second)
    {
        var position = 0;
        while (position < pattern.Length)
        {
            var open = pattern.IndexOf('{', position, StringComparison.Ordinal);
            if (open < 0 || open + 2 >= pattern.Length || pattern[open + 2] != '}' || pattern[open + 1] is not ('0' or '1'))
            {
                if (open < 0)
                {
                    AddLiteral(result, pattern[position..]);
                    return;
                }

                // Not a placeable: keep the brace as a literal
                AddLiteral(result, pattern[position..(open + 1)]);
                position = open + 1;
                continue;
            }

            if (open > position)
            {
                AddLiteral(result, pattern[position..open]);
            }

            result.AddRange(pattern[open + 1] == '0' ? first : second);
            position = open + 3;
        }
    }

    private static void AddLiteral(List<ListPart> result, string value)
    {
        if (result.Count > 0 && result[^1].Type is "literal")
        {
            result[^1] = new ListPart("literal", result[^1].Value + value);
        }
        else
        {
            result.Add(new ListPart("literal", value));
        }
    }
}
