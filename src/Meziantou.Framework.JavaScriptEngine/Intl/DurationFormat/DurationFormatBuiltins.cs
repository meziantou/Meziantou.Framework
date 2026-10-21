using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>A Duration Unit Options Record.</summary>
internal readonly record struct DurationUnitOptions(string Style, string Display);

/// <summary>A part of a formatted duration.</summary>
internal readonly record struct DurationPart(string Type, string Value, string? Unit);

/// <summary>An Intl.DurationFormat instance: an ordinary object with the [[InitializedDurationFormat]] internal slot.</summary>
internal sealed class JsDurationFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    private readonly Dictionary<string, JsNumberFormatObject> _numberFormats = new(StringComparer.Ordinal);

    public string Locale { get; set; } = "";

    public string NumberingSystem { get; set; } = "latn";

    /// <summary>[[Style]]: "long", "short", "narrow" or "digital".</summary>
    public string Style { get; set; } = "short";

    /// <summary>[[YearsOptions]] to [[NanosecondsOptions]], in the order of <see cref="DurationFormatBuiltins.Units"/>.</summary>
    public DurationUnitOptions[] UnitOptions { get; } = new DurationUnitOptions[10];

    public string HourMinuteSeparator { get; set; } = ":";

    public string MinuteSecondSeparator { get; set; } = ":";

    /// <summary>[[FractionalDigits]], or null when undefined.</summary>
    public int? FractionalDigits { get; set; }

    /// <summary>Gets or sets the list patterns of the unit lists (ListFormatParts).</summary>
    public ListPatterns? ListPatterns { get; set; }

    /// <summary>
    /// Gets the NumberFormat created with the given options by the abstract operations of DurationFormat. The options
    /// objects have a null prototype, so creating the NumberFormat is not observable and it can be cached.
    /// </summary>
    public JsNumberFormatObject GetNumberFormat(string key, Func<JsObject> createOptions)
    {
        if (!_numberFormats.TryGetValue(key, out var numberFormat))
        {
            numberFormat = new JsNumberFormatObject(JsEngine.Current!.CurrentRealm!.Intrinsics.IntlNumberFormatPrototype);
            NumberFormatBuiltins.Initialize(numberFormat, Locale, createOptions());
            _numberFormats[key] = numberFormat;
        }

        return numberFormat;
    }
}

/// <summary>Intl.DurationFormat (https://tc39.es/ecma402/#durationformat-objects).</summary>
internal static class DurationFormatBuiltins
{
    private const int Hours = 4;
    private const int Minutes = 5;
    private const int Seconds = 6;
    private const int Milliseconds = 7;

    private static readonly string[] RelevantExtensionKeys = ["nu"];

    /// <summary>The Unit column of the table of the units.</summary>
    internal static readonly string[] Units = ["years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds", "nanoseconds"];

    /// <summary>The NumberFormat Unit column of the table of the units.</summary>
    private static readonly string[] NumberFormatUnits = ["year", "month", "week", "day", "hour", "minute", "second", "millisecond", "microsecond", "nanosecond"];

    private static readonly string[] DateStyles = ["long", "short", "narrow"];
    private static readonly string[] TimeStyles = ["long", "short", "narrow", "numeric", "2-digit"];
    private static readonly string[] SubsecondStyles = ["long", "short", "narrow", "numeric"];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("DurationFormat", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.DurationFormat", PropertyFlags.Configurable);
        realm.DefineMethod(prototype, "resolvedOptions", 0, ResolvedOptions);
        realm.DefineMethod(prototype, "format", 1, Format);
        realm.DefineMethod(prototype, "formatToParts", 1, FormatToParts);
        intl.DefineBuiltIn("DurationFormat", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.DurationFormat([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.DurationFormat requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlDurationFormatPrototype);
        var durationFormat = new JsDurationFormatObject(prototype);

        // ResolveOptions
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var options = IntlOperations.GetOptionsObject(arguments.Length > 1 ? arguments[1] : default);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var numberingSystem = IntlOperations.GetStringOption(options, "numberingSystem", [], fallback: null);
        if (numberingSystem is not null && !LanguageTag.IsUnicodeType(numberingSystem))
            ThrowHelper.ThrowRangeError("Invalid numberingSystem : " + numberingSystem);

        var r = IntlOperations.ResolveLocale(requestedLocales, RelevantExtensionKeys, _ => numberingSystem, static (locale, _) => NumberFormatBuiltins.GetNumberingSystemLocaleData(locale));
        durationFormat.Locale = r.Locale;
        durationFormat.NumberingSystem = r.Get("nu") ?? "latn";

        // [[DigitalFormat]]: the time separator of the numbering system; the hours are not padded (as ICU)
        var dataLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        var timeSeparator = dataLocale.GetNumberSymbols(durationFormat.NumberingSystem).TimeSeparator;
        if (timeSeparator.Length == 0)
        {
            timeSeparator = ":";
        }

        durationFormat.HourMinuteSeparator = timeSeparator;
        durationFormat.MinuteSecondSeparator = timeSeparator;
        const bool TwoDigitHours = false;

        var style = IntlOperations.GetStringOption(options, "style", ["long", "short", "narrow", "digital"], "short")!;
        durationFormat.Style = style;
        var prevStyle = "";
        for (var i = 0; i < Units.Length; i++)
        {
            var styles = i switch
            {
                < Hours => DateStyles,
                < Milliseconds => TimeStyles,
                _ => SubsecondStyles,
            };

            var digitalBase = i < Hours ? "short" : "numeric";
            var unitOptions = GetDurationUnitOptions(Units[i], options, style, styles, digitalBase, prevStyle, TwoDigitHours);
            durationFormat.UnitOptions[i] = unitOptions;
            if (i is >= Hours and <= 8)
            {
                prevStyle = unitOptions.Style;
            }
        }

        durationFormat.FractionalDigits = IntlOperations.GetNumberOption(options, "fractionalDigits", 0, 9, fallback: null);
        return durationFormat;
    }

    /// <summary>GetDurationUnitOptions(unit, options, baseStyle, stylesList, digitalBase, prevStyle, twoDigitHours)</summary>
    private static DurationUnitOptions GetDurationUnitOptions(string unit, JsObject options, string baseStyle, string[] stylesList, string digitalBase, string prevStyle, bool twoDigitHours)
    {
        var style = IntlOperations.GetStringOption(options, unit, stylesList, fallback: null);
        var displayDefault = "always";
        if (style is null)
        {
            if (baseStyle is "digital")
            {
                style = digitalBase;
                if (unit is not ("hours" or "minutes" or "seconds"))
                {
                    displayDefault = "auto";
                }
            }
            else if (prevStyle is "fractional" or "numeric" or "2-digit")
            {
                style = "numeric";
                if (unit is not ("minutes" or "seconds"))
                {
                    displayDefault = "auto";
                }
            }
            else
            {
                style = baseStyle;
                displayDefault = "auto";
            }
        }

        if (style is "numeric" && IsFractionalSecondUnitName(unit))
        {
            style = "fractional";
            displayDefault = "auto";
        }

        var display = IntlOperations.GetStringOption(options, unit + "Display", ["auto", "always"], displayDefault)!;

        // ValidateDurationUnitStyle
        if (display is "always" && style is "fractional")
            ThrowHelper.ThrowRangeError($"The {unit}Display option cannot be \"always\" when {unit} is fractional");

        if (prevStyle is "fractional" && style is not "fractional")
            ThrowHelper.ThrowRangeError($"The style of {unit} must be fractional when the previous unit is fractional");

        if (prevStyle is "numeric" or "2-digit" && style is not ("fractional" or "numeric" or "2-digit"))
            ThrowHelper.ThrowRangeError($"The style of {unit} must be numeric when the previous unit is numeric");

        if (unit is "hours" && twoDigitHours)
        {
            style = "2-digit";
        }

        if (unit is "minutes" or "seconds" && prevStyle is "numeric" or "2-digit")
        {
            style = "2-digit";
        }

        return new DurationUnitOptions(style, display);
    }

    private static bool IsFractionalSecondUnitName(string unit) => unit is "milliseconds" or "microseconds" or "nanoseconds";

    private static JsDurationFormatObject ThisDurationFormat(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsDurationFormatObject durationFormat)
            return durationFormat;

        return ThrowHelper.ThrowTypeError<JsDurationFormatObject>($"Intl.DurationFormat.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>Intl.DurationFormat.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var durationFormat = ThisDurationFormat(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", durationFormat.Locale);
        options.CreateDataPropertyOrThrow("numberingSystem", durationFormat.NumberingSystem);
        options.CreateDataPropertyOrThrow("style", durationFormat.Style);
        for (var i = 0; i < Units.Length; i++)
        {
            var unitOptions = durationFormat.UnitOptions[i];
            options.CreateDataPropertyOrThrow(Units[i], unitOptions.Style is "fractional" ? "numeric" : unitOptions.Style);
            options.CreateDataPropertyOrThrow(Units[i] + "Display", unitOptions.Display);
        }

        if (durationFormat.FractionalDigits is { } fractionalDigits)
        {
            options.CreateDataPropertyOrThrow("fractionalDigits", fractionalDigits);
        }

        return options;
    }

    /// <summary>Intl.DurationFormat.prototype.format(duration)</summary>
    private static JsValue Format(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var durationFormat = ThisDurationFormat(thisValue, "format");
        var record = ToDurationRecord(arguments.Length > 0 ? arguments[0] : default);
        var parts = PartitionDurationFormatPattern(durationFormat, record);
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.Value);
        }

        return builder.ToString();
    }

    /// <summary>Intl.DurationFormat.prototype.formatToParts(duration)</summary>
    private static JsValue FormatToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var durationFormat = ThisDurationFormat(thisValue, "formatToParts");
        var record = ToDurationRecord(arguments.Length > 0 ? arguments[0] : default);
        var parts = PartitionDurationFormatPattern(durationFormat, record);
        var realm = function.Realm;
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Value);
            if (parts[i].Unit is { } unit)
            {
                o.CreateDataPropertyOrThrow("unit", unit);
            }

            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    /// <summary>ToDurationRecord(input): the fields of a Temporal.Duration, of a duration string, or of a duration-like object.</summary>
    internal static double[] ToDurationRecord(JsValue input)
    {
        if (!input.IsObject)
        {
            if (input.Cell is JsString)
            {
                var parsed = TemporalOperations.ToTemporalDuration(input);
                return GetFields(parsed);
            }

            return ThrowHelper.ThrowTypeError<double[]>("The duration must be an object");
        }

        if (input.Cell is JsDuration duration)
            return GetFields(duration);

        var obj = input.TryGetObject()!;
        var result = new double[10];
        var found = false;
        Read("days", 3);
        Read("hours", 4);
        Read("microseconds", 8);
        Read("milliseconds", 7);
        Read("minutes", 5);
        Read("months", 1);
        Read("nanoseconds", 9);
        Read("seconds", 6);
        Read("weeks", 2);
        Read("years", 0);
        if (!found)
            ThrowHelper.ThrowTypeError("The duration must have at least one of the properties years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds or nanoseconds");

        if (!IsValidDuration(result))
            ThrowHelper.ThrowRangeError("The duration is not valid");

        return result;

        void Read(string name, int index)
        {
            var value = obj.Get(name);
            if (value.IsUndefined)
                return;

            found = true;
            var number = JsOperations.ToNumber(value);
            if (!double.IsFinite(number) || Math.Truncate(number) != number)
                ThrowHelper.ThrowRangeError($"The {name} of the duration must be an integer");

            result[index] = number + 0.0;
        }

        static double[] GetFields(JsDuration duration) =>
        [
            duration.Years, duration.Months, duration.Weeks, duration.Days, duration.Hours, duration.Minutes,
            duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds,
        ];
    }

    /// <summary>IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds)</summary>
    private static bool IsValidDuration(double[] fields)
    {
        var sign = 0;
        foreach (var v in fields)
        {
            if (!double.IsFinite(v))
                return false;

            if (v < 0)
            {
                if (sign > 0)
                    return false;

                sign = -1;
            }
            else if (v > 0)
            {
                if (sign < 0)
                    return false;

                sign = 1;
            }
        }

        const double TwoToThe32 = 4294967296;
        if (Math.Abs(fields[0]) >= TwoToThe32 || Math.Abs(fields[1]) >= TwoToThe32 || Math.Abs(fields[2]) >= TwoToThe32)
            return false;

        // The normalized seconds, exactly, in nanoseconds
        var nanoseconds = (new BigInteger(fields[3]) * 86_400 + new BigInteger(fields[4]) * 3600 + new BigInteger(fields[5]) * 60 + new BigInteger(fields[6])) * 1_000_000_000
            + new BigInteger(fields[7]) * 1_000_000 + new BigInteger(fields[8]) * 1_000 + new BigInteger(fields[9]);
        var limit = BigInteger.Pow(2, 53) * 1_000_000_000;
        return BigInteger.Abs(nanoseconds) < limit;
    }

    /// <summary>DurationSign(duration)</summary>
    private static int DurationSign(double[] duration)
    {
        foreach (var v in duration)
        {
            if (v < 0)
                return -1;

            if (v > 0)
                return 1;
        }

        return 0;
    }

    /// <summary>
    /// The value of a unit plus ComputeFractionalDigits(durationFormat, duration): the values of the units with the
    /// "fractional" style after it, as a fraction of the unit (exact).
    /// </summary>
    private static IntlDecimal AddFractionalDigits(JsDurationFormatObject durationFormat, double[] duration, double value)
    {
        var total = new BigInteger(value) * 1_000_000_000;
        var exponent = 3;
        for (var i = Milliseconds; i < Units.Length; i++)
        {
            if (durationFormat.UnitOptions[i].Style is "fractional")
            {
                total += new BigInteger(duration[i]) * IntlDecimal.PowerOfTen(9 - exponent);
                exponent += 3;
            }
        }

        return new IntlDecimal(total.Sign < 0, BigInteger.Abs(total), -9);
    }

    /// <summary>NextUnitFractional(durationFormat, unit)</summary>
    private static bool NextUnitFractional(JsDurationFormatObject durationFormat, int unit)
    {
        return unit is >= Seconds and <= 8 && durationFormat.UnitOptions[unit + 1].Style is "fractional";
    }

    private static IntlDecimal ToDecimal(double value, bool negativeZero)
    {
        if (value == 0)
            return negativeZero ? new IntlDecimal(isNegative: true, BigInteger.Zero, 0) : IntlDecimal.Zero;

        return IntlDecimal.FromDouble(value);
    }

    /// <summary>FormatNumericHours, FormatNumericMinutes and FormatNumericSeconds (without the separator)</summary>
    private static void FormatNumericUnit(List<DurationPart> result, JsDurationFormatObject durationFormat, int unit, IntlDecimal value, bool signDisplayed)
    {
        var style = durationFormat.UnitOptions[unit].Style;
        var twoDigits = style is "2-digit";
        var isSeconds = unit == Seconds;
        var key = "numeric:" + (twoDigits ? "2" : "1") + (signDisplayed ? "s" : "n") + (isSeconds ? "f" : "");
        var nf = durationFormat.GetNumberFormat(key, () =>
        {
            var nfOptions = new JsObject(prototype: null);
            nfOptions.CreateDataPropertyOrThrow("numberingSystem", durationFormat.NumberingSystem);
            if (twoDigits)
            {
                nfOptions.CreateDataPropertyOrThrow("minimumIntegerDigits", 2);
            }

            if (!signDisplayed)
            {
                nfOptions.CreateDataPropertyOrThrow("signDisplay", "never");
            }

            nfOptions.CreateDataPropertyOrThrow("useGrouping", JsValue.False);
            if (isSeconds)
            {
                AddFractionDigitsOptions(durationFormat, nfOptions);
            }

            return nfOptions;
        });

        var numberFormatUnit = NumberFormatUnits[unit];
        foreach (var part in NumberFormatter.PartitionNumberPattern(nf, value).ToList())
        {
            result.Add(new DurationPart(part.Type, part.Value, numberFormatUnit));
        }
    }

    private static void AddFractionDigitsOptions(JsDurationFormatObject durationFormat, JsObject nfOptions)
    {
        if (durationFormat.FractionalDigits is { } fractionDigits)
        {
            nfOptions.CreateDataPropertyOrThrow("maximumFractionDigits", fractionDigits);
            nfOptions.CreateDataPropertyOrThrow("minimumFractionDigits", fractionDigits);
        }
        else
        {
            nfOptions.CreateDataPropertyOrThrow("maximumFractionDigits", 9);
            nfOptions.CreateDataPropertyOrThrow("minimumFractionDigits", 0);
        }

        nfOptions.CreateDataPropertyOrThrow("roundingMode", "trunc");
    }

    /// <summary>FormatNumericUnits(durationFormat, duration, firstNumericUnit, signDisplayed)</summary>
    private static List<DurationPart> FormatNumericUnits(JsDurationFormatObject durationFormat, double[] duration, int firstNumericUnit, bool signDisplayed)
    {
        var numericPartsList = new List<DurationPart>();
        var hoursValue = duration[Hours];
        var hoursDisplay = durationFormat.UnitOptions[Hours].Display;
        var minutesValue = duration[Minutes];
        var minutesDisplay = durationFormat.UnitOptions[Minutes].Display;
        var secondsValue = duration[Milliseconds] != 0 || duration[8] != 0 || duration[9] != 0
            ? AddFractionalDigits(durationFormat, duration, duration[Seconds])
            : ToDecimal(duration[Seconds], negativeZero: false);
        var secondsDisplay = durationFormat.UnitOptions[Seconds].Display;

        var hoursFormatted = firstNumericUnit == Hours && (hoursValue != 0 || hoursDisplay is "always");
        var secondsFormatted = !secondsValue.IsZero || secondsDisplay is "always";
        var minutesFormatted = false;
        if (firstNumericUnit is Hours or Minutes)
        {
            if (hoursFormatted && secondsFormatted)
            {
                minutesFormatted = true;
            }
            else if (minutesValue != 0 || minutesDisplay is "always")
            {
                minutesFormatted = true;
            }
        }

        if (hoursFormatted)
        {
            var value = ToDecimal(hoursValue, signDisplayed && hoursValue == 0 && DurationSign(duration) == -1);
            FormatNumericUnit(numericPartsList, durationFormat, Hours, value, signDisplayed);
            signDisplayed = false;
        }

        if (minutesFormatted)
        {
            var value = ToDecimal(minutesValue, signDisplayed && minutesValue == 0 && DurationSign(duration) == -1);
            if (hoursFormatted)
            {
                numericPartsList.Add(new DurationPart("literal", durationFormat.HourMinuteSeparator, Unit: null));
            }

            FormatNumericUnit(numericPartsList, durationFormat, Minutes, value, signDisplayed);
            signDisplayed = false;
        }

        if (secondsFormatted)
        {
            if (minutesFormatted)
            {
                numericPartsList.Add(new DurationPart("literal", durationFormat.MinuteSecondSeparator, Unit: null));
            }

            FormatNumericUnit(numericPartsList, durationFormat, Seconds, secondsValue, signDisplayed);
        }

        return numericPartsList;
    }

    /// <summary>PartitionDurationFormatPattern(durationFormat, duration)</summary>
    internal static List<DurationPart> PartitionDurationFormatPattern(JsDurationFormatObject durationFormat, double[] duration)
    {
        var result = new List<List<DurationPart>>();
        var signDisplayed = true;
        for (var i = 0; i < Units.Length; i++)
        {
            var (style, display) = durationFormat.UnitOptions[i];
            if (style is "numeric" or "2-digit")
            {
                var numericPartsList = FormatNumericUnits(durationFormat, duration, i, signDisplayed);
                if (numericPartsList.Count > 0)
                {
                    result.Add(numericPartsList);
                }

                break;
            }

            var nextUnitFractional = NextUnitFractional(durationFormat, i);
            var value = nextUnitFractional ? AddFractionalDigits(durationFormat, duration, duration[i]) : ToDecimal(duration[i], negativeZero: false);
            if (display is "always" || !value.IsZero)
            {
                var hideSign = !signDisplayed;
                if (signDisplayed)
                {
                    signDisplayed = false;
                    if (value.IsZero && DurationSign(duration) == -1)
                    {
                        value = new IntlDecimal(isNegative: true, BigInteger.Zero, 0);
                    }
                }

                var numberFormatUnit = NumberFormatUnits[i];
                var key = "unit:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + (hideSign ? "n" : "s") + (nextUnitFractional ? "f" : "");
                var nf = durationFormat.GetNumberFormat(key, () =>
                {
                    var nfOptions = new JsObject(prototype: null);
                    if (nextUnitFractional)
                    {
                        AddFractionDigitsOptions(durationFormat, nfOptions);
                    }

                    nfOptions.CreateDataPropertyOrThrow("numberingSystem", durationFormat.NumberingSystem);
                    if (hideSign)
                    {
                        nfOptions.CreateDataPropertyOrThrow("signDisplay", "never");
                    }

                    nfOptions.CreateDataPropertyOrThrow("style", "unit");
                    nfOptions.CreateDataPropertyOrThrow("unit", numberFormatUnit);
                    nfOptions.CreateDataPropertyOrThrow("unitDisplay", style);
                    return nfOptions;
                });

                var list = new List<DurationPart>();
                foreach (var part in NumberFormatter.PartitionNumberPattern(nf, value).ToList())
                {
                    list.Add(new DurationPart(part.Type, part.Value, numberFormatUnit));
                }

                result.Add(list);
            }

            if (nextUnitFractional)
                break;
        }

        return ListFormatParts(durationFormat, result);
    }

    /// <summary>ListFormatParts(durationFormat, partitionedPartsList)</summary>
    private static List<DurationPart> ListFormatParts(JsDurationFormatObject durationFormat, List<List<DurationPart>> partitionedPartsList)
    {
        if (durationFormat.ListPatterns is null)
        {
            var listStyle = durationFormat.Style is "digital" ? "short" : durationFormat.Style;
            var r = IntlOperations.ResolveLocale([durationFormat.Locale], [], static _ => null, static (_, _) => []);
            durationFormat.ListPatterns = new ListPatterns(IntlOperations.Data.FindLocale(r.DataLocale), "unit", listStyle);
        }

        var strings = new string[partitionedPartsList.Count];
        var builder = new StringBuilder();
        for (var i = 0; i < strings.Length; i++)
        {
            builder.Clear();
            foreach (var part in partitionedPartsList[i])
            {
                builder.Append(part.Value);
            }

            strings[i] = builder.ToString();
        }

        var formattedPartsList = ListFormatBuiltins.CreatePartsFromList(durationFormat.ListPatterns, strings);
        var partitionedPartsIndex = 0;
        var flattenedPartsList = new List<DurationPart>();
        foreach (var listPart in formattedPartsList)
        {
            if (listPart.Type is "element")
            {
                flattenedPartsList.AddRange(partitionedPartsList[partitionedPartsIndex]);
                partitionedPartsIndex++;
            }
            else
            {
                flattenedPartsList.Add(new DurationPart("literal", listPart.Value, Unit: null));
            }
        }

        return flattenedPartsList;
    }
}
