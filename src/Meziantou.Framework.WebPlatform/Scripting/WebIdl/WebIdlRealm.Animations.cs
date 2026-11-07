using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The keyframes of Web Animations (https://drafts.csswg.org/web-animations-1/#processing-a-keyframes-argument): the property
// values are the members of the keyframe objects, named after the IDL attributes of the properties (e.g. marginLeft)
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private static readonly string[] CompositeOperationOrAutoValues = ["replace", "add", "accumulate", "auto"];

    /// <summary>The animatable properties by IDL attribute name (https://drafts.csswg.org/web-animations-1/#animation-property-name-to-idl-attribute-name).</summary>
    private static readonly Lazy<FrozenDictionary<string, string>> AnimatablePropertiesByIdlName = new(static () =>
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in CssPropertyRegistry.Properties)
        {
            // The legacy aliases without vendor prefix are accepted, like in the browsers (e.g. fontStretch for font-width)
            if ((property.AliasOf is not null && property.Name.StartsWith('-', StringComparison.Ordinal)) || !KeyframeProcessing.IsAnimatableProperty(property.Name))
                continue;

            result[ToIdlAttributeName(property.Name)] = property.Name;
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    });

    /// <summary>https://drafts.csswg.org/web-animations-1/#animation-property-name-to-idl-attribute-name</summary>
    private static string ToIdlAttributeName(string property)
    {
        if (property == "float")
            return "cssFloat";

        if (property == "offset")
            return "cssOffset";

        if (CssPropertyRegistry.IsCustomProperty(property))
            return property;

        // https://drafts.csswg.org/cssom/#css-property-to-idl-attribute
        var builder = new System.Text.StringBuilder(property.Length);
        var uppercaseNext = false;
        foreach (var c in property)
        {
            if (c == '-')
            {
                uppercaseNext = true;
                continue;
            }

            builder.Append(uppercaseNext ? char.ToUpperInvariant(c) : c);
            uppercaseNext = false;
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/web-animations-1/#processing-a-keyframes-argument</summary>
    internal List<Keyframe> ToKeyframes(TValue? value)
    {
        var result = new List<Keyframe>();
        if (IsNullOrUndefined(value))
            return result;

        if (!Adapter.IsObject(value))
            throw CreateTypeError("The keyframes are not an object");

        var method = Adapter.Get(value, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator));
        if (!IsNullOrUndefined(method))
        {
            if (!Adapter.IsCallable(method))
                throw CreateTypeError("The @@iterator member of the keyframes is not a function");

            var iterator = Adapter.Call(method, value, []);
            if (!Adapter.IsObject(iterator))
                throw CreateTypeError("The iterator is not an object");

            var next = Adapter.Get(iterator, Key("next"));
            while (true)
            {
                var step = Adapter.Call(next, iterator, []);
                if (!Adapter.IsObject(step))
                    throw CreateTypeError("The iterator result is not an object");

                if (Adapter.ToBoolean(Adapter.Get(step, Key("done"))))
                    break;

                var item = Adapter.Get(step, Key("value"));
                if (!IsNullOrUndefined(item) && !Adapter.IsObject(item))
                    throw CreateTypeError("A keyframe is not an object");

                var (offset, easing, composite, properties) = ProcessKeyframeLikeObject(item, allowLists: false);
                result.Add(new Keyframe
                {
                    Offset = offset is [double single] ? single : null,
                    RangeOffset = offset is [TimelineRangeBoundary rangeOffset] ? rangeOffset : null,
                    Easing = easing is [var singleEasing] ? singleEasing : "linear",
                    Composite = composite is [var singleComposite] ? singleComposite : CompositeOperationOrAuto.Auto,
                    Properties = properties.ToDictionary(p => p.Key, p => p.Value[0], StringComparer.Ordinal),
                });
            }

            return result;
        }

        return ToPropertyIndexedKeyframes(value);
    }

    /// <summary>Processes a property-indexed keyframe object (e.g. <c>{ opacity: [0, 1], offset: [0, 1] }</c>).</summary>
    private List<Keyframe> ToPropertyIndexedKeyframes(TValue value)
    {
        var (offsets, easings, composites, properties) = ProcessKeyframeLikeObject(value, allowLists: true);

        // The keyframes of each property, spaced evenly, merged by offset
        var keyframes = new List<(double ComputedOffset, Dictionary<string, string> Properties)>();
        foreach (var (property, values) in properties)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var computedOffset = values.Count == 1 ? 1 : (double)i / (values.Count - 1);
                var index = keyframes.FindIndex(k => k.ComputedOffset == computedOffset);
                if (index < 0)
                {
                    keyframes.Add((computedOffset, new Dictionary<string, string>(StringComparer.Ordinal)));
                    index = keyframes.Count - 1;
                }

                keyframes[index].Properties[property] = values[i];
            }
        }

        keyframes.Sort(static (a, b) => a.ComputedOffset.CompareTo(b.ComputedOffset));

        // The easings are repeated to the number of keyframes; the unused easings must be valid
        if (easings.Count == 0)
        {
            easings = ["linear"];
        }

        for (var i = keyframes.Count; i < easings.Count; i++)
        {
            _ = AnimationEffect.ParseEasing(easings[i]);
        }

        var result = new List<Keyframe>(keyframes.Count);
        for (var i = 0; i < keyframes.Count; i++)
        {
            result.Add(new Keyframe
            {
                Offset = i < offsets.Count && offsets[i] is double offset ? offset : null,
                RangeOffset = i < offsets.Count ? offsets[i] as TimelineRangeBoundary : null,
                Easing = easings[i % easings.Count],
                Composite = composites.Count == 0 ? CompositeOperationOrAuto.Auto : composites[i % composites.Count],
                Properties = keyframes[i].Properties,
            });
        }

        return result;
    }

    /// <summary>
    /// https://drafts.csswg.org/web-animations-1/#process-a-keyframe-like-object: the members of BaseKeyframe (or of
    /// BasePropertyIndexedKeyframe when <paramref name="allowLists"/> is true) in lexicographical order, then the animatable
    /// properties in Unicode code point order.
    /// </summary>
    private (List<object?> Offsets, List<string> Easings, List<CompositeOperationOrAuto> Composites, List<KeyValuePair<string, List<string>>> Properties) ProcessKeyframeLikeObject(TValue? value, bool allowLists)
    {
        var offsets = new List<object?>();
        var easings = new List<string>();
        var composites = new List<CompositeOperationOrAuto>();
        var properties = new List<KeyValuePair<string, List<string>>>();
        if (IsNullOrUndefined(value))
            return (allowLists ? offsets : [null], allowLists ? easings : ["linear"], allowLists ? composites : [CompositeOperationOrAuto.Auto], properties);

        // The members of the dictionary, in lexicographical order: composite, easing, offset
        var composite = Adapter.Get(value, Key("composite"));
        if (Adapter.IsUndefined(composite))
        {
            if (!allowLists)
            {
                composites.Add(CompositeOperationOrAuto.Auto);
            }
        }
        else if (allowLists && IsIterable(composite))
        {
            composites.AddRange(ToSequence(composite, static (r, e) => r.ToCompositeOperationOrAuto(e)));
        }
        else
        {
            composites.Add(ToCompositeOperationOrAuto(composite));
        }

        var easing = Adapter.Get(value, Key("easing"));
        if (Adapter.IsUndefined(easing))
        {
            if (!allowLists)
            {
                easings.Add("linear");
            }
        }
        else if (allowLists && IsIterable(easing))
        {
            easings.AddRange(ToSequence(easing, static (r, e) => r.ToDomString(e, legacyNullToEmptyString: false)));
        }
        else
        {
            easings.Add(ToDomString(easing, legacyNullToEmptyString: false));
        }

        var offset = Adapter.Get(value, Key("offset"));
        if (Adapter.IsUndefined(offset))
        {
            if (!allowLists)
            {
                offsets.Add(null);
            }
        }
        else if (allowLists && IsIterable(offset))
        {
            offsets.AddRange(ToSequence(offset, static (r, e) => r.Adapter.IsNull(e) || r.Adapter.IsUndefined(e) ? null : r.ToKeyframeOffset(e)));
        }
        else
        {
            offsets.Add(Adapter.IsNull(offset) ? null : ToKeyframeOffset(offset));
        }

        // The animatable properties and the custom properties, in Unicode code point order
        var names = new List<(string IdlName, string Property)>();
        var animatableProperties = AnimatablePropertiesByIdlName.Value;
        foreach (var key in Adapter.OwnKeys(value))
        {
            if (!Adapter.IsString(key) || !Adapter.GetOwnProperty(value, key, out var descriptor) || !descriptor.Enumerable)
                continue;

            var name = Adapter.AsString(key);
            if (animatableProperties.TryGetValue(name, out var property))
            {
                names.Add((name, property));
            }
            else if (CssPropertyRegistry.IsCustomProperty(name))
            {
                names.Add((name, name));
            }
        }

        names.Sort(static (a, b) => string.CompareOrdinal(a.IdlName, b.IdlName));
        foreach (var (idlName, property) in names)
        {
            var raw = Adapter.Get(value, Key(idlName));
            List<string> values;
            if (allowLists && IsIterable(raw))
            {
                values = ToSequence(raw, static (r, e) => r.ToDomString(e, legacyNullToEmptyString: false));
            }
            else
            {
                values = [ToDomString(raw, legacyNullToEmptyString: false)];
            }

            properties.Add(new(property, values));
        }

        return (offsets, easings, composites, properties);
    }

    /// <summary>
    /// Converts a keyframe offset: a number, or, like the browsers, a string that is a CSS number or percentage (e.g. "calc(0.5)").
    /// A timeline offset (https://drafts.csswg.org/web-animations-2/#keyframe-offset-type) is a string "&lt;timeline-range-name&gt;
    /// &lt;percentage&gt;" (e.g. "entry 10%") or a <see cref="TimelineRangeOffset"/> object: it is returned as a <see cref="TimelineRangeBoundary"/>.
    /// </summary>
    private object ToKeyframeOffset(TValue value)
    {
        if (Adapter.IsObject(value) && Unwrap(value) is null)
        {
            var rangeName = Adapter.Get(value, Key("rangeName"));
            var rangeOffset = Adapter.Get(value, Key("offset"));
            var name = IsNullOrUndefined(rangeName) ? null : ToDomString(rangeName, legacyNullToEmptyString: false);
            var numeric = IsNullOrUndefined(rangeOffset) ? null : Unwrap(rangeOffset) as Css.TypedOm.CssNumericValue ?? throw CreateTypeError("The offset of a timeline offset must be a CSSNumericValue");
            return TimelineRangeBoundary.FromKeyframeOffset(name, numeric) ?? throw CreateTypeError("The timeline offset of the keyframe is invalid");
        }

        if (Adapter.IsString(value))
        {
            var components = Meziantou.Framework.WebPlatform.Css.Syntax.CssParser.ParseListOfComponentValues(Adapter.AsString(value));
            var significant = Meziantou.Framework.WebPlatform.Css.Values.ValueMatcher.RemoveWhitespace(components);
            if (significant is [Meziantou.Framework.WebPlatform.Css.Syntax.CssPreservedToken { Type: Meziantou.Framework.WebPlatform.Css.Syntax.CssTokenType.Ident }, ..])
                return TimelineRangeBoundary.ParseKeyframeSelector(significant) ?? throw CreateTypeError("'" + Adapter.AsString(value) + "' is not a valid keyframe offset");

            if (significant is [var component])
            {
                switch (component)
                {
                    case Meziantou.Framework.WebPlatform.Css.Syntax.CssPreservedToken { Type: Meziantou.Framework.WebPlatform.Css.Syntax.CssTokenType.Number } number:
                        return number.Token.NumericValue;
                    case Meziantou.Framework.WebPlatform.Css.Syntax.CssPreservedToken { Type: Meziantou.Framework.WebPlatform.Css.Syntax.CssTokenType.Percentage } percentage:
                        return percentage.Token.NumericValue / 100;
                    case Meziantou.Framework.WebPlatform.Css.Syntax.CssFunction function when CssCalc.Evaluate(function, CssCalcContext.Default) is { IsResolved: true } calc && calc.Unit is "" or "%":
                        return calc.Unit == "%" ? calc.Value / 100 : calc.Value;
                }
            }
        }

        return ToDouble(value, restricted: true);
    }

    private bool IsIterable(TValue value) => Adapter.IsObject(value) && !IsNullOrUndefined(Adapter.Get(value, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator)));

    private CompositeOperationOrAuto ToCompositeOperationOrAuto(TValue value) => ToEnumerationValue(value, "CompositeOperationOrAuto", CompositeOperationOrAutoValues) switch
    {
        "replace" => CompositeOperationOrAuto.Replace,
        "add" => CompositeOperationOrAuto.Add,
        "accumulate" => CompositeOperationOrAuto.Accumulate,
        _ => CompositeOperationOrAuto.Auto,
    };

    /// <summary>
    /// https://drafts.csswg.org/web-animations-1/#dom-keyframeeffect-getkeyframes: an array of objects with the members of
    /// BaseComputedKeyframe and the property values, named after the IDL attributes of the properties.
    /// </summary>
    internal TValue FromComputedKeyframes(IReadOnlyList<ComputedKeyframe> keyframes)
    {
        var items = new TValue[keyframes.Count];
        for (var i = 0; i < keyframes.Count; i++)
        {
            var keyframe = keyframes[i];
            var item = CreateObject();
            DefineDataProperty(item, "composite", Adapter.FromString(keyframe.Composite switch
            {
                CompositeOperationOrAuto.Replace => "replace",
                CompositeOperationOrAuto.Add => "add",
                CompositeOperationOrAuto.Accumulate => "accumulate",
                _ => "auto",
            }));
            DefineDataProperty(item, "computedOffset", keyframe.IsOffsetUnresolved ? Adapter.Null : Adapter.FromNumber(keyframe.ComputedOffset));
            DefineDataProperty(item, "easing", Adapter.FromString(keyframe.Easing));
            DefineDataProperty(item, "offset", keyframe.RangeOffset is { } rangeOffset ? ToJs(TimelineRangeBoundary.ToScriptValue(rangeOffset)) : FromNumber(keyframe.Offset));
            foreach (var (property, propertyValue) in keyframe.Properties)
            {
                DefineDataProperty(item, ToIdlAttributeName(property), Adapter.FromString(propertyValue));
            }

            items[i] = item;
        }

        return Adapter.CreateArray(items);
    }

    private void DefineDataProperty(TValue target, string name, TValue value) => Adapter.DefineOwnProperty(target, Key(name), JsPropertyDescriptor<TValue>.Data(value, writable: true, enumerable: true, configurable: true));
}
