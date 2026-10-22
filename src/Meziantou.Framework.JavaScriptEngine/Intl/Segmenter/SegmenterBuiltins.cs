namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The granularity of a segmenter.</summary>
internal enum SegmenterGranularity
{
    Grapheme,
    Word,
    Sentence,
}

/// <summary>An Intl.Segmenter instance: an ordinary object with the [[InitializedSegmenter]] internal slot.</summary>
internal sealed class JsSegmenterObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    /// <summary>[[SegmenterGranularity]]</summary>
    public SegmenterGranularity Granularity { get; set; }
}

/// <summary>
/// The boundaries of a string for a granularity (UAX #29, without the dictionaries of ICU: the ideographs and the
/// scripts written without spaces are not split into words), computed once for the Segments object and its iterators.
/// </summary>
internal sealed class SegmentBoundaries
{
    private readonly List<int> _boundaries;
    private readonly List<byte>? _wordKinds;

    public SegmentBoundaries(string text, SegmenterGranularity granularity)
    {
        _boundaries = [0];
        switch (granularity)
        {
            case SegmenterGranularity.Grapheme:
                foreach (var range in Unicode.EnumerateGraphemeClusters(text))
                {
                    _boundaries.Add(range.End.Value);
                }

                break;

            case SegmenterGranularity.Word:
                _wordKinds = [];
                var enumerator = Unicode.EnumerateWordBoundaries(text);
                while (enumerator.MoveNext())
                {
                    _boundaries.Add(enumerator.Current.End.Value);
                    _wordKinds.Add((byte)enumerator.CurrentKind);
                }

                break;

            default:
                foreach (var range in Unicode.EnumerateSentenceBoundaries(text))
                {
                    _boundaries.Add(range.End.Value);
                }

                break;
        }
    }

    /// <summary>Gets the index of the segment that contains a code unit (0 &lt;= index &lt; length).</summary>
    public int FindSegment(int index)
    {
        // The last boundary that is less than or equal to index
        var low = 0;
        var high = _boundaries.Count - 2;
        while (low < high)
        {
            var middle = (low + high + 1) >>> 1;
            if (_boundaries[middle] <= index)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    public int GetStart(int segment) => _boundaries[segment];

    public int GetEnd(int segment) => _boundaries[segment + 1];

    /// <summary>Gets whether a segment of the word granularity is word-like (a number, a word of letters, kana or ideographs).</summary>
    public bool IsWordLike(int segment) => _wordKinds is not null && (UnicodeWordKind)_wordKinds[segment] is not UnicodeWordKind.None;
}

/// <summary>A Segments object (https://tc39.es/ecma402/#sec-segments-objects).</summary>
internal sealed class JsSegmentsObject(JsObject prototype, JsSegmenterObject segmenter, JsString input) : JsObject(prototype, ObjectClass.Intl)
{
    private SegmentBoundaries? _boundaries;

    /// <summary>[[SegmentsSegmenter]]</summary>
    public JsSegmenterObject Segmenter { get; } = segmenter;

    /// <summary>[[SegmentsString]]</summary>
    public JsString Input { get; } = input;

    public string Text { get; } = input.ToString();

    public SegmentBoundaries Boundaries => _boundaries ??= new SegmentBoundaries(Text, Segmenter.Granularity);
}

/// <summary>A Segment Iterator (https://tc39.es/ecma402/#sec-segment-iterator-objects).</summary>
internal sealed class JsSegmentIteratorObject(JsObject prototype, JsSegmentsObject segments) : JsObject(prototype, ObjectClass.Intl)
{
    /// <summary>[[IteratingSegmenter]] and [[IteratedString]] (with the boundaries of the Segments object).</summary>
    public JsSegmentsObject Segments { get; } = segments;

    /// <summary>[[IteratedStringNextSegmentCodeUnitIndex]]</summary>
    public int NextIndex { get; set; }
}

/// <summary>Intl.Segmenter (https://tc39.es/ecma402/#segmenter-objects).</summary>
internal static class SegmenterBuiltins
{
    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("resolvedOptions", 0, ResolvedOptions),
        new("segment", 1, Segment),
    ];

    private static readonly BuiltinMethodTable SegmentsPrototypeMethods =
    [
        new("containing", 1, Containing),
        new(WellKnownSymbols.Iterator, 0, SegmentsIterator),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl, JsObject prototype, JsObject segmentsPrototype, JsObject segmentIteratorPrototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("Segmenter", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.Segmenter", PropertyFlags.Configurable);
        realm.DefineMethods(prototype, PrototypeMethods);

        realm.DefineMethods(segmentsPrototype, SegmentsPrototypeMethods);

        realm.DefineMethod(segmentIteratorPrototype, "next", 0, Next);
        segmentIteratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Segmenter String Iterator", PropertyFlags.Configurable);
        intl.DefineBuiltIn("Segmenter", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.Segmenter([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.Segmenter requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlSegmenterPrototype);
        var segmenter = new JsSegmenterObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var options = IntlOperations.GetOptionsObject(arguments.Length > 1 ? arguments[1] : default);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var r = IntlOperations.ResolveLocale(requestedLocales, [], static _ => null, static (_, _) => []);
        segmenter.Locale = r.Locale;
        segmenter.Granularity = IntlOperations.GetStringOption(options, "granularity", ["grapheme", "word", "sentence"], "grapheme") switch
        {
            "word" => SegmenterGranularity.Word,
            "sentence" => SegmenterGranularity.Sentence,
            _ => SegmenterGranularity.Grapheme,
        };

        return segmenter;
    }

    private static JsSegmenterObject ThisSegmenter(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsSegmenterObject segmenter)
            return segmenter;

        return ThrowHelper.ThrowTypeError<JsSegmenterObject>($"Intl.Segmenter.prototype.{method} called on an incompatible receiver");
    }

    private static string GetGranularityName(SegmenterGranularity granularity) => granularity switch
    {
        SegmenterGranularity.Word => "word",
        SegmenterGranularity.Sentence => "sentence",
        _ => "grapheme",
    };

    /// <summary>Intl.Segmenter.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var segmenter = ThisSegmenter(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", segmenter.Locale);
        options.CreateDataPropertyOrThrow("granularity", GetGranularityName(segmenter.Granularity));
        return options;
    }

    /// <summary>Intl.Segmenter.prototype.segment(string)</summary>
    private static JsValue Segment(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var segmenter = ThisSegmenter(thisValue, "segment");
        var text = JsOperations.ToJsString(arguments.Length > 0 ? arguments[0] : default);
        return new JsSegmentsObject(function.Realm.Intrinsics.IntlSegmentsPrototype, segmenter, text);
    }

    /// <summary>%IntlSegmentsPrototype%.containing(index)</summary>
    private static JsValue Containing(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsSegmentsObject segments)
            return ThrowHelper.ThrowTypeError<JsValue>("%Segments.prototype%.containing called on an incompatible receiver");

        var n = JsOperations.ToIntegerOrInfinity(arguments.Length > 0 ? arguments[0] : default);
        if (n < 0 || n >= segments.Text.Length)
            return JsValue.Undefined;

        var boundaries = segments.Boundaries;
        var segment = boundaries.FindSegment((int)n);
        return CreateSegmentDataObject(function.Realm, segments, segment);
    }

    /// <summary>%IntlSegmentsPrototype%[@@iterator]()</summary>
    private static JsValue SegmentsIterator(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsSegmentsObject segments)
            return ThrowHelper.ThrowTypeError<JsValue>("%Segments.prototype%[@@iterator] called on an incompatible receiver");

        return new JsSegmentIteratorObject(function.Realm.Intrinsics.IntlSegmentIteratorPrototype, segments);
    }

    /// <summary>%IntlSegmentIteratorPrototype%.next()</summary>
    private static JsValue Next(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsSegmentIteratorObject iterator)
            return ThrowHelper.ThrowTypeError<JsValue>("%SegmentIterator.prototype%.next called on an incompatible receiver");

        var segments = iterator.Segments;
        var startIndex = iterator.NextIndex;
        if (startIndex >= segments.Text.Length)
            return IteratorOperations.CreateIteratorResultObject(function.Realm, JsValue.Undefined, done: true);

        var segment = segments.Boundaries.FindSegment(startIndex);
        iterator.NextIndex = segments.Boundaries.GetEnd(segment);
        return IteratorOperations.CreateIteratorResultObject(function.Realm, CreateSegmentDataObject(function.Realm, segments, segment), done: false);
    }

    /// <summary>CreateSegmentDataObject(segmenter, string, startIndex, endIndex)</summary>
    private static JsObject CreateSegmentDataObject(JsRealm realm, JsSegmentsObject segments, int segment)
    {
        var boundaries = segments.Boundaries;
        var startIndex = boundaries.GetStart(segment);
        var endIndex = boundaries.GetEnd(segment);
        var result = new JsObject(realm.Intrinsics.ObjectPrototype);
        result.CreateDataPropertyOrThrow("segment", segments.Text[startIndex..endIndex]);
        result.CreateDataPropertyOrThrow("index", startIndex);
        result.CreateDataPropertyOrThrow("input", JsValue.FromJsString(segments.Input));
        if (segments.Segmenter.Granularity is SegmenterGranularity.Word)
        {
            result.CreateDataPropertyOrThrow("isWordLike", boundaries.IsWordLike(segment) ? JsValue.True : JsValue.False);
        }

        return result;
    }
}
