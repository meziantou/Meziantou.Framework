namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>
/// The categories of the locale data (Intl/Data/locales.bin). Each one is a data tree per locale whose paths are the
/// ones of cldr-json below the given JSON path (the keys are the cldr-json keys, the segments of a path are separated
/// by '/'). Each locale stores only the values that differ from its parent; <see cref="CldrLocale"/> resolves the
/// inheritance. The data of a category and a language is in its own chunk, so it is decompressed only when used.
/// </summary>
internal enum CldrCategory
{
    /// <summary>
    /// <c>numbers</c> of numbers.json, without <c>currencies</c> and <c>minimalPairs</c>: defaultNumberingSystem,
    /// otherNumberingSystems, minimumGroupingDigits, symbols-numberSystem-latn/decimal,
    /// decimalFormats-numberSystem-latn/standard, decimalFormats-numberSystem-latn/short/decimalFormat/1000-count-one,
    /// scientificFormats-numberSystem-*, percentFormats-numberSystem-*,
    /// currencyFormats-numberSystem-latn/{standard,accounting,short,currencySpacing,unitPattern-count-*},
    /// miscPatterns-numberSystem-*/{approximately,atLeast,atMost,range}.
    /// </summary>
    Numbers,

    /// <summary><c>numbers/currencies</c> of currencies.json: EUR/{displayName, displayName-count-one, symbol, symbol-alt-narrow}.</summary>
    Currencies,

    /// <summary>
    /// <c>units</c> of units.json, limited to the units of ECMA-402 (IsSanctionedSingleUnitIdentifier) whose key is the
    /// identifier of ECMA-402 (e.g. "kilometer" instead of "length-kilometer"):
    /// {long,short,narrow}/kilometer/{displayName, unitPattern-count-one, perUnitPattern},
    /// {long,short,narrow}/per/compoundUnitPattern, durationUnit-type-{hm,hms,ms}/durationUnitPattern.
    /// </summary>
    Units,

    /// <summary>
    /// <c>dates/calendars/gregorian</c> of ca-gregorian.json: months/format/wide/1, days/stand-alone/abbreviated/sun,
    /// quarters, dayPeriods/format/wide/am, eras/eraAbbr/1, dateFormats/full, timeFormats/short,
    /// dateTimeFormats/{full,...,availableFormats/yMMMd,appendItems,intervalFormats/yMMMd/d}, dateSkeletons, timeSkeletons.
    /// </summary>
    Gregorian,

    /// <summary>
    /// <c>dates/calendars</c> without gregorian (ca-generic.json and the cldr-cal-* packages): generic, iso8601,
    /// buddhist, chinese, coptic, dangi, ethiopic, ethiopic-amete-alem, hebrew, indian, islamic, islamic-civil,
    /// islamic-rgsa, islamic-tbla, islamic-umalqura, japanese, persian, roc; each with the structure of gregorian.
    /// </summary>
    Calendars,

    /// <summary>
    /// <c>dates/timeZoneNames</c> of timeZoneNames.json: hourFormat, gmtFormat, gmtZeroFormat, gmtUnknownFormat,
    /// regionFormat, regionFormat-type-{daylight,standard}, fallbackFormat,
    /// zone/Europe/Paris/{exemplarCity,long/daylight}, metazone/Europe_Central/{long,short}/{generic,standard,daylight}.
    /// </summary>
    TimeZoneNames,

    /// <summary>
    /// <c>dates/fields</c> of dateFields.json: year/{displayName,relative-type--1,relativeTime-type-future/relativeTimePattern-count-one},
    /// year-short, year-narrow, quarter, month, week, day, hour, minute, second, sun, ..., dayperiod, zone.
    /// </summary>
    Fields,

    /// <summary>
    /// <c>localeDisplayNames</c> of languages.json, scripts.json, territories.json, variants.json and
    /// localeDisplayNames.json (without subdivisions): languages/fr, languages/en-GB, languages/en-alt-short,
    /// scripts/Latn, territories/FR, territories/HK-alt-short, variants/VALENCIA, keys/calendar,
    /// types/calendar/gregorian, localeDisplayPattern/{localePattern,localeSeparator,localeKeyTypePattern}, codePatterns.
    /// </summary>
    DisplayNames,

    /// <summary>
    /// listPatterns.json and layout.json: listPatterns/listPattern-type-{standard,or,unit}[-short|-narrow]/{start,middle,end,2},
    /// layout/orientation/{characterOrder,lineOrder}.
    /// </summary>
    Misc,
}
