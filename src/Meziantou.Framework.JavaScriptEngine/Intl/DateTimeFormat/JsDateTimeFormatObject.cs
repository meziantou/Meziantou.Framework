using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.DateTimeFormat instance: an ordinary object with the [[InitializedDateTimeFormat]] internal slot.</summary>
internal sealed class JsDateTimeFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    private string[]? _digits;
    private string? _timeSeparator;
    private string? _decimalSeparator;

    /// <summary>[[Locale]]</summary>
    public string Locale { get; set; } = "";

    /// <summary>Gets or sets the data of the locale and the calendar.</summary>
    public DateTimeLocaleData Data { get; set; } = null!;

    /// <summary>[[Calendar]]</summary>
    public string Calendar { get; set; } = "gregory";

    /// <summary>[[NumberingSystem]]</summary>
    public string NumberingSystem { get; set; } = "latn";

    /// <summary>[[TimeZone]]: the identifier (an available named time zone identifier, as cased in the database, or an offset).</summary>
    public string TimeZoneIdentifier { get; set; } = "UTC";

    /// <summary>Gets or sets the time zone of [[TimeZone]].</summary>
    public TemporalTimeZone TimeZone { get; set; } = null!;

    /// <summary>[[HourCycle]]</summary>
    public string? HourCycle { get; set; }

    /// <summary>[[DateStyle]]</summary>
    public string? DateStyle { get; set; }

    /// <summary>[[TimeStyle]]</summary>
    public string? TimeStyle { get; set; }

    /// <summary>[[DateTimeFormat]]</summary>
    public DateTimeFormatRecord Format { get; set; } = null!;

    /// <summary>[[TemporalPlainDateFormat]]</summary>
    public DateTimeFormatRecord? PlainDateFormat { get; set; }

    /// <summary>[[TemporalPlainYearMonthFormat]]</summary>
    public DateTimeFormatRecord? PlainYearMonthFormat { get; set; }

    /// <summary>[[TemporalPlainMonthDayFormat]]</summary>
    public DateTimeFormatRecord? PlainMonthDayFormat { get; set; }

    /// <summary>[[TemporalPlainTimeFormat]]</summary>
    public DateTimeFormatRecord? PlainTimeFormat { get; set; }

    /// <summary>[[TemporalPlainDateTimeFormat]]</summary>
    public DateTimeFormatRecord PlainDateTimeFormat { get; set; } = null!;

    /// <summary>[[TemporalInstantFormat]]</summary>
    public DateTimeFormatRecord InstantFormat { get; set; } = null!;

    /// <summary>[[BoundFormat]]</summary>
    public JsFunction? BoundFormat { get; set; }

    /// <summary>Gets the digits of the numbering system (0 to 9), or null for "latn".</summary>
    public string[]? Digits
    {
        get
        {
            if (NumberingSystem is "latn")
                return null;

            return _digits ??= DateTimeFormatter.GetDigitStrings(CldrData.Default.Supplemental.GetNumberingSystemDigits(NumberingSystem));
        }
    }

    /// <summary>Gets the decimal separator of the numbering system (between the seconds and the fractional seconds).</summary>
    public string DecimalSeparator => _decimalSeparator ??= Data.Locale.GetNumberSymbols(NumberingSystem).Decimal is { Length: > 0 } separator ? separator : ".";

    /// <summary>Gets the time separator of the numbering system.</summary>
    public string TimeSeparator => _timeSeparator ??= Data.Locale.GetNumberSymbols(NumberingSystem).TimeSeparator is { Length: > 0 } separator ? separator : ":";
}
