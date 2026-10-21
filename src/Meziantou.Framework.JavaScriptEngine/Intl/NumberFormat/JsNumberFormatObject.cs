using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.NumberFormat instance: an ordinary object with the [[InitializedNumberFormat]] internal slot.</summary>
internal sealed class JsNumberFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    private Data.PluralRules? _pluralRules;
    private NumberSymbols? _symbols;
    private string? _digits;

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

    public Data.PluralRules PluralRules => _pluralRules ??= CldrData.Default.Supplemental.GetPluralRules(DataLocale.Name, PluralRuleType.Cardinal);
}
