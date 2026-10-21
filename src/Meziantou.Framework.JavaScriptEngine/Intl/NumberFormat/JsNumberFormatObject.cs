using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.NumberFormat instance: an ordinary object with the [[InitializedNumberFormat]] internal slot.</summary>
internal sealed class JsNumberFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    private Data.PluralRules? _pluralRules;
    private NumberSymbols? _symbols;
    private string? _digits;
    private string? _currencyDecimal;
    private string? _currencyGroup;

    public string Locale { get; set; } = "";

    /// <summary>Gets or sets the locale of the data (the locale without the Unicode extension).</summary>
    public CldrLocale DataLocale { get; set; } = null!;

    public string NumberingSystem { get; set; } = "latn";

    /// <summary>[[Style]]: "decimal", "percent", "currency" or "unit".</summary>
    public string Style { get; set; } = "decimal";

    /// <summary>[[Currency]] (upper case), or null.</summary>
    public string? Currency { get; set; }

    /// <summary>[[CurrencyDisplay]]: "code", "symbol", "narrowSymbol" or "name".</summary>
    public string CurrencyDisplay { get; set; } = "symbol";

    /// <summary>[[CurrencySign]]: "standard" or "accounting".</summary>
    public string CurrencySign { get; set; } = "standard";

    /// <summary>[[Unit]], or null.</summary>
    public string? Unit { get; set; }

    /// <summary>[[UnitDisplay]]: "short", "narrow" or "long".</summary>
    public string UnitDisplay { get; set; } = "short";

    /// <summary>[[Notation]]: "standard", "scientific", "engineering" or "compact".</summary>
    public string Notation { get; set; } = "standard";

    /// <summary>[[CompactDisplay]]: "short" or "long".</summary>
    public string CompactDisplay { get; set; } = "short";

    /// <summary>[[UseGrouping]]: "always", "auto", "min2", or null for false.</summary>
    public string? UseGrouping { get; set; } = "auto";

    /// <summary>[[SignDisplay]]: "auto", "never", "always", "exceptZero" or "negative".</summary>
    public string SignDisplay { get; set; } = "auto";

    public DigitOptions Digits { get; } = new();

    /// <summary>[[BoundFormat]]</summary>
    public JsFunction? BoundFormat { get; set; }

    public NumberSymbols Symbols => _symbols ??= DataLocale.GetNumberSymbols(NumberingSystem);

    /// <summary>Gets the digits of the numbering system (0 to 9), or null for "latn".</summary>
    public string? NumberingSystemDigits
    {
        get
        {
            if (NumberingSystem is "latn")
                return null;

            return _digits ??= CldrData.Default.Supplemental.GetNumberingSystemDigits(NumberingSystem) ?? "";
        }
    }

    /// <summary>Gets or sets the cached number pattern of the style.</summary>
    public NumberPattern? StylePattern { get; set; }

    /// <summary>Gets or sets the cached decimal pattern (its grouping sizes are used for every style).</summary>
    public NumberPattern? DecimalPattern { get; set; }

    /// <summary>Gets or sets the cached alphaNextToNumber variant of the currency pattern, used next to a digit, or null.</summary>
    public NumberPattern? CurrencyAlphaNextToNumberPattern { get; set; }

    /// <summary>Gets whether the style is "unit" with the unit "percent", formatted with the percent pattern.</summary>
    public bool IsPercentUnit => Style is "unit" && Unit is "percent";

    /// <summary>Gets the decimal separator of the currencies (currencyDecimal, else decimal).</summary>
    public string CurrencyDecimal => _currencyDecimal ??= GetCurrencySymbol("currencyDecimal") ?? Symbols.Decimal;

    /// <summary>Gets the grouping separator of the currencies (currencyGroup, else group).</summary>
    public string CurrencyGroup => _currencyGroup ??= GetCurrencySymbol("currencyGroup") ?? Symbols.Group;

    /// <summary>Gets or sets the cached text of the currency (code, symbol or narrow symbol).</summary>
    public string? CurrencyText { get; set; }

    /// <summary>Gets or sets the cached minimumGroupingDigits of the locale.</summary>
    public int? MinimumGroupingDigits { get; set; }

    private string? GetCurrencySymbol(string name)
    {
        var prefix = DataLocale.Contains(CldrCategory.Numbers, "symbols-numberSystem-" + NumberingSystem) ? "symbols-numberSystem-" + NumberingSystem : "symbols-numberSystem-latn";
        return DataLocale.GetString(CldrCategory.Numbers, prefix + "/" + name);
    }

    public Data.PluralRules PluralRules => _pluralRules ??= CldrData.Default.Supplemental.GetPluralRules(DataLocale.Name, PluralRuleType.Cardinal);
}
