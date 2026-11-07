namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The abstract operations of Temporal (https://tc39.es/proposal-temporal/).</summary>
internal static partial class TemporalOperations
{
    public const int PrecisionAuto = -1;
    public const int PrecisionMinute = -2;

    private static readonly string[] OverflowValues = ["constrain", "reject"];
    private static readonly string[] DisambiguationValues = ["compatible", "earlier", "later", "reject"];
    private static readonly string[] OffsetValues = ["prefer", "use", "ignore", "reject"];
    private static readonly string[] ShowCalendarValues = ["auto", "always", "never", "critical"];
    private static readonly string[] ShowTimeZoneValues = ["auto", "never", "critical"];
    private static readonly string[] ShowOffsetValues = ["auto", "never"];
    private static readonly string[] DirectionValues = ["next", "previous"];
    private static readonly string[] RoundingModeValues = ["ceil", "floor", "expand", "trunc", "halfCeil", "halfFloor", "halfExpand", "halfTrunc", "halfEven"];

    private static readonly string[] SingularUnitNames = ["year", "month", "week", "day", "hour", "minute", "second", "millisecond", "microsecond", "nanosecond"];
    private static readonly string[] PluralUnitNames = ["years", "months", "weeks", "days", "hours", "minutes", "seconds", "milliseconds", "microseconds", "nanoseconds"];

    /// <summary>Gets the intrinsics of the current realm (the realm of the running built-in function).</summary>
    public static Intrinsics CurrentIntrinsics => JsEngine.Current!.CurrentRealm!.Intrinsics;

    public static JsValue Argument(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    // ------------------------------------------------------------------------------------------------------------------
    // Type conversion
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ToIntegerWithTruncation(argument)</summary>
    public static double ToIntegerWithTruncation(JsValue argument)
    {
        var number = argument.IsNumber ? argument.NumberValue : JsOperations.ToNumber(argument);
        if (!double.IsFinite(number))
            return ThrowHelper.ThrowRangeError<double>("The value " + NumberFormatting.ToString(number) + " is not a finite number");

        return Math.Truncate(number) + 0.0;
    }

    /// <summary>ToPositiveIntegerWithTruncation(argument)</summary>
    public static double ToPositiveIntegerWithTruncation(JsValue argument)
    {
        var integer = ToIntegerWithTruncation(argument);
        if (integer <= 0)
            return ThrowHelper.ThrowRangeError<double>("The value " + NumberFormatting.ToString(integer) + " must be a positive integer");

        return integer;
    }

    /// <summary>ToIntegerIfIntegral(argument)</summary>
    public static double ToIntegerIfIntegral(JsValue argument)
    {
        var number = argument.IsNumber ? argument.NumberValue : JsOperations.ToNumber(argument);
        if (!NumberBuiltins.IsIntegralNumber(number))
            return ThrowHelper.ThrowRangeError<double>("The value " + NumberFormatting.ToString(number) + " is not an integer");

        return number + 0.0;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Options
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>GetOptionsObject(options)</summary>
    public static JsObject GetOptionsObject(JsValue options)
    {
        if (options.IsUndefined)
            return new JsObject(prototype: null);

        if (options.Cell is JsObject obj)
            return obj;

        return ThrowHelper.ThrowTypeError<JsObject>("The options must be an object or undefined");
    }

    /// <summary>GetOption(options, property, string, values, default): null when the value is undefined and there is no default.</summary>
    public static string? GetStringOption(JsObject options, string property, ReadOnlySpan<string> values, string? defaultValue, bool required = false)
    {
        var value = options.Get(property);
        if (value.IsUndefined)
        {
            if (required)
                return ThrowHelper.ThrowRangeError<string>("The option '" + property + "' is required");

            return defaultValue;
        }

        var result = JsOperations.ToString(value);
        if (values.Length > 0 && !values.Contains(result))
            return ThrowHelper.ThrowRangeError<string>("The value '" + result + "' is not valid for the option '" + property + "'");

        return result;
    }

    /// <summary>GetTemporalOverflowOption(options)</summary>
    public static TemporalOverflow GetTemporalOverflowOption(JsObject options)
    {
        return GetStringOption(options, "overflow", OverflowValues, "constrain") == "constrain" ? TemporalOverflow.Constrain : TemporalOverflow.Reject;
    }

    /// <summary>GetTemporalDisambiguationOption(options)</summary>
    public static TemporalDisambiguation GetTemporalDisambiguationOption(JsObject options)
    {
        return GetStringOption(options, "disambiguation", DisambiguationValues, "compatible") switch
        {
            "compatible" => TemporalDisambiguation.Compatible,
            "earlier" => TemporalDisambiguation.Earlier,
            "later" => TemporalDisambiguation.Later,
            _ => TemporalDisambiguation.Reject,
        };
    }

    /// <summary>GetTemporalOffsetOption(options, fallback)</summary>
    public static TemporalOffsetOption GetTemporalOffsetOption(JsObject options, TemporalOffsetOption fallback)
    {
        var stringFallback = fallback switch
        {
            TemporalOffsetOption.Prefer => "prefer",
            TemporalOffsetOption.Use => "use",
            TemporalOffsetOption.Ignore => "ignore",
            _ => "reject",
        };

        return GetStringOption(options, "offset", OffsetValues, stringFallback) switch
        {
            "prefer" => TemporalOffsetOption.Prefer,
            "use" => TemporalOffsetOption.Use,
            "ignore" => TemporalOffsetOption.Ignore,
            _ => TemporalOffsetOption.Reject,
        };
    }

    /// <summary>GetTemporalShowCalendarNameOption(options)</summary>
    public static ShowCalendarOption GetTemporalShowCalendarNameOption(JsObject options)
    {
        return GetStringOption(options, "calendarName", ShowCalendarValues, "auto") switch
        {
            "always" => ShowCalendarOption.Always,
            "never" => ShowCalendarOption.Never,
            "critical" => ShowCalendarOption.Critical,
            _ => ShowCalendarOption.Auto,
        };
    }

    /// <summary>GetTemporalShowTimeZoneNameOption(options)</summary>
    public static ShowTimeZoneOption GetTemporalShowTimeZoneNameOption(JsObject options)
    {
        return GetStringOption(options, "timeZoneName", ShowTimeZoneValues, "auto") switch
        {
            "never" => ShowTimeZoneOption.Never,
            "critical" => ShowTimeZoneOption.Critical,
            _ => ShowTimeZoneOption.Auto,
        };
    }

    /// <summary>GetTemporalShowOffsetOption(options): true for auto, false for never.</summary>
    public static bool GetTemporalShowOffsetOption(JsObject options) => GetStringOption(options, "offset", ShowOffsetValues, "auto") != "never";

    /// <summary>GetDirectionOption(options): true for next, false for previous.</summary>
    public static bool GetDirectionOption(JsObject options) => GetStringOption(options, "direction", DirectionValues, defaultValue: null, required: true) == "next";

    /// <summary>GetRoundingModeOption(options, fallback)</summary>
    public static TemporalRoundingMode GetRoundingModeOption(JsObject options, TemporalRoundingMode fallback)
    {
        var value = GetStringOption(options, "roundingMode", RoundingModeValues, RoundingModeValues[(int)fallback]);
        return (TemporalRoundingMode)Array.IndexOf(RoundingModeValues, value);
    }

    /// <summary>GetRoundingIncrementOption(options)</summary>
    public static int GetRoundingIncrementOption(JsObject options)
    {
        var value = options.Get("roundingIncrement");
        if (value.IsUndefined)
            return 1;

        var integerIncrement = ToIntegerWithTruncation(value);
        if (integerIncrement is < 1 or > 1_000_000_000)
            return ThrowHelper.ThrowRangeError<int>("The rounding increment " + NumberFormatting.ToString(integerIncrement) + " is out of range");

        return (int)integerIncrement;
    }

    /// <summary>ValidateTemporalRoundingIncrement(increment, dividend, inclusive)</summary>
    public static void ValidateTemporalRoundingIncrement(long increment, long dividend, bool inclusive)
    {
        var maximum = inclusive ? dividend : dividend - 1;
        if (increment > maximum)
            ThrowHelper.ThrowRangeError("The rounding increment " + increment.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is out of range");

        if (dividend % increment != 0)
            ThrowHelper.ThrowRangeError("The rounding increment " + increment.ToString(System.Globalization.CultureInfo.InvariantCulture) + " does not divide " + dividend.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>GetTemporalFractionalSecondDigitsOption(options): an integer from 0 to 9, or <see cref="PrecisionAuto"/>.</summary>
    public static int GetTemporalFractionalSecondDigitsOption(JsObject options)
    {
        var digitsValue = options.Get("fractionalSecondDigits");
        if (digitsValue.IsUndefined)
            return PrecisionAuto;

        if (!digitsValue.IsNumber)
        {
            if (JsOperations.ToString(digitsValue) != "auto")
                return ThrowHelper.ThrowRangeError<int>("The value of fractionalSecondDigits must be 'auto' or an integer from 0 to 9");

            return PrecisionAuto;
        }

        var number = digitsValue.NumberValue;
        if (!double.IsFinite(number))
            return ThrowHelper.ThrowRangeError<int>("The value of fractionalSecondDigits must be 'auto' or an integer from 0 to 9");

        var digitCount = Math.Floor(number);
        if (digitCount is < 0 or > 9)
            return ThrowHelper.ThrowRangeError<int>("The value of fractionalSecondDigits must be 'auto' or an integer from 0 to 9");

        return (int)digitCount;
    }

    /// <summary>ToSecondsStringPrecisionRecord(smallestUnit, fractionalDigitCount)</summary>
    public static SecondsStringPrecision ToSecondsStringPrecisionRecord(TemporalUnit smallestUnit, int fractionalDigitCount)
    {
        switch (smallestUnit)
        {
            case TemporalUnit.Minute:
                return new SecondsStringPrecision(PrecisionMinute, TemporalUnit.Minute, 1);
            case TemporalUnit.Second:
                return new SecondsStringPrecision(0, TemporalUnit.Second, 1);
            case TemporalUnit.Millisecond:
                return new SecondsStringPrecision(3, TemporalUnit.Millisecond, 1);
            case TemporalUnit.Microsecond:
                return new SecondsStringPrecision(6, TemporalUnit.Microsecond, 1);
            case TemporalUnit.Nanosecond:
                return new SecondsStringPrecision(9, TemporalUnit.Nanosecond, 1);
        }

        if (fractionalDigitCount == PrecisionAuto)
            return new SecondsStringPrecision(PrecisionAuto, TemporalUnit.Nanosecond, 1);

        if (fractionalDigitCount == 0)
            return new SecondsStringPrecision(0, TemporalUnit.Second, 1);

        if (fractionalDigitCount <= 3)
            return new SecondsStringPrecision(fractionalDigitCount, TemporalUnit.Millisecond, Pow10(3 - fractionalDigitCount));

        if (fractionalDigitCount <= 6)
            return new SecondsStringPrecision(fractionalDigitCount, TemporalUnit.Microsecond, Pow10(6 - fractionalDigitCount));

        return new SecondsStringPrecision(fractionalDigitCount, TemporalUnit.Nanosecond, Pow10(9 - fractionalDigitCount));

        static int Pow10(int exponent)
        {
            var result = 1;
            for (var i = 0; i < exponent; i++)
            {
                result *= 10;
            }

            return result;
        }
    }

    /// <summary>GetTemporalUnitValuedOption(options, key, default): <see cref="TemporalUnit.Unset"/> or <see cref="TemporalUnit.Auto"/> for the special values.</summary>
    public static TemporalUnit GetTemporalUnitValuedOption(JsObject options, string key, bool required)
    {
        var value = options.Get(key);
        if (value.IsUndefined)
        {
            if (required)
                return ThrowHelper.ThrowRangeError<TemporalUnit>("The option '" + key + "' is required");

            return TemporalUnit.Unset;
        }

        var text = JsOperations.ToString(value);
        if (text == "auto")
            return TemporalUnit.Auto;

        var index = Array.IndexOf(SingularUnitNames, text);
        if (index < 0)
        {
            index = Array.IndexOf(PluralUnitNames, text);
        }

        if (index < 0)
            return ThrowHelper.ThrowRangeError<TemporalUnit>("The value '" + text + "' is not a valid unit for the option '" + key + "'");

        return (TemporalUnit)index;
    }

    /// <summary>ValidateTemporalUnitValue(value, unitGroup, extraValues)</summary>
    public static void ValidateTemporalUnitValue(TemporalUnit value, TemporalUnitGroup unitGroup, TemporalUnit extraValue1 = TemporalUnit.Unset, TemporalUnit extraValue2 = TemporalUnit.Unset)
    {
        if (value == TemporalUnit.Unset)
            return;

        if (value == extraValue1 || value == extraValue2)
            return;

        if (value == TemporalUnit.Auto)
        {
            ThrowHelper.ThrowRangeError("The unit 'auto' is not allowed here");
            return;
        }

        var isDate = IsDateUnit(value);
        if (isDate && unitGroup is TemporalUnitGroup.Date or TemporalUnitGroup.DateTime)
            return;

        if (!isDate && unitGroup is TemporalUnitGroup.Time or TemporalUnitGroup.DateTime)
            return;

        ThrowHelper.ThrowRangeError("The unit '" + GetUnitName(value) + "' is not allowed here");
    }

    public static string GetUnitName(TemporalUnit unit) => unit is TemporalUnit.Auto ? "auto" : SingularUnitNames[(int)unit];

    /// <summary>TemporalUnitCategory(unit) is date.</summary>
    public static bool IsDateUnit(TemporalUnit unit) => unit <= TemporalUnit.Day;

    /// <summary>IsCalendarUnit(unit)</summary>
    public static bool IsCalendarUnit(TemporalUnit unit) => unit <= TemporalUnit.Week;

    /// <summary>LargerOfTwoTemporalUnits(u1, u2)</summary>
    public static TemporalUnit LargerOfTwoTemporalUnits(TemporalUnit u1, TemporalUnit u2) => u1 <= u2 ? u1 : u2;

    /// <summary>MaximumTemporalDurationRoundingIncrement(unit): 0 for unset.</summary>
    public static int MaximumTemporalDurationRoundingIncrement(TemporalUnit unit)
    {
        return unit switch
        {
            TemporalUnit.Hour => 24,
            TemporalUnit.Minute or TemporalUnit.Second => 60,
            TemporalUnit.Millisecond or TemporalUnit.Microsecond or TemporalUnit.Nanosecond => 1000,
            _ => 0,
        };
    }

    /// <summary>GetDifferenceSettings(operation, options, unitGroup, disallowedUnits, fallbackSmallestUnit, smallestLargestDefaultUnit)</summary>
    public static DifferenceSettings GetDifferenceSettings(DifferenceOperation operation, JsObject options, TemporalUnitGroup unitGroup, bool disallowWeekAndDay, TemporalUnit fallbackSmallestUnit, TemporalUnit smallestLargestDefaultUnit)
    {
        var largestUnit = GetTemporalUnitValuedOption(options, "largestUnit", required: false);
        var roundingIncrement = GetRoundingIncrementOption(options);
        var roundingMode = GetRoundingModeOption(options, TemporalRoundingMode.Trunc);
        var smallestUnit = GetTemporalUnitValuedOption(options, "smallestUnit", required: false);
        ValidateTemporalUnitValue(largestUnit, unitGroup, TemporalUnit.Auto);
        if (largestUnit == TemporalUnit.Unset)
        {
            largestUnit = TemporalUnit.Auto;
        }

        if (disallowWeekAndDay && largestUnit is TemporalUnit.Week or TemporalUnit.Day)
            ThrowHelper.ThrowRangeError("The unit '" + GetUnitName(largestUnit) + "' is not allowed here");

        ValidateTemporalUnitValue(smallestUnit, unitGroup);
        if (smallestUnit == TemporalUnit.Unset)
        {
            smallestUnit = fallbackSmallestUnit;
        }

        if (disallowWeekAndDay && smallestUnit is TemporalUnit.Week or TemporalUnit.Day)
            ThrowHelper.ThrowRangeError("The unit '" + GetUnitName(smallestUnit) + "' is not allowed here");

        var defaultLargestUnit = LargerOfTwoTemporalUnits(smallestLargestDefaultUnit, smallestUnit);
        if (largestUnit == TemporalUnit.Auto)
        {
            largestUnit = defaultLargestUnit;
        }

        if (LargerOfTwoTemporalUnits(largestUnit, smallestUnit) != largestUnit)
            ThrowHelper.ThrowRangeError("The largest unit '" + GetUnitName(largestUnit) + "' is smaller than the smallest unit '" + GetUnitName(smallestUnit) + "'");

        var maximum = MaximumTemporalDurationRoundingIncrement(smallestUnit);
        if (maximum != 0)
        {
            ValidateTemporalRoundingIncrement(roundingIncrement, maximum, inclusive: false);
        }

        if (operation == DifferenceOperation.Since)
        {
            roundingMode = TemporalMath.NegateRoundingMode(roundingMode);
        }

        return new DifferenceSettings(smallestUnit, largestUnit, roundingMode, roundingIncrement);
    }

    /// <summary>
    /// Converts the roundTo / totalOf parameter of round() and total(): a String becomes an object with a single property
    /// (OrdinaryObjectCreate(null)), undefined is a TypeError, and the other values go through GetOptionsObject.
    /// </summary>
    public static JsObject GetRoundToOptions(JsValue value, string propertyName)
    {
        if (value.IsUndefined)
            return ThrowHelper.ThrowTypeError<JsObject>("The options are required");

        if (value.Cell is JsString)
        {
            var result = new JsObject(prototype: null);
            result.CreateDataPropertyOrThrow(propertyName, value);
            return result;
        }

        return GetOptionsObject(value);
    }

    /// <summary>IsPartialTemporalObject(value)</summary>
    public static bool IsPartialTemporalObject(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return false;

        if (obj is JsPlainDate or JsPlainDateTime or JsPlainMonthDay or JsPlainTime or JsPlainYearMonth or JsZonedDateTime)
            return false;

        if (!obj.Get("calendar").IsUndefined)
            return false;

        if (!obj.Get("timeZone").IsUndefined)
            return false;

        return true;
    }
}
