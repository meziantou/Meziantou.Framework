using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intl;
    private JsObject? _intlLocalePrototype;
    private JsFunction? _intlLocale;
    private JsObject? _intlPluralRulesPrototype;
    private JsObject? _intlNumberFormatPrototype;
    private JsFunction? _intlNumberFormat;
    private JsNumberFormatObject? _defaultNumberFormat;

    /// <summary>%Intl%</summary>
    public JsObject Intl => _intl!;

    /// <summary>%Intl.Locale.prototype%</summary>
    public JsObject IntlLocalePrototype => _intlLocalePrototype!;

    /// <summary>%Intl.Locale%</summary>
    public JsFunction IntlLocale => _intlLocale!;

    /// <summary>%Intl.PluralRules.prototype%</summary>
    public JsObject IntlPluralRulesPrototype => _intlPluralRulesPrototype!;

    /// <summary>%Intl.NumberFormat.prototype%</summary>
    public JsObject IntlNumberFormatPrototype => _intlNumberFormatPrototype!;

    /// <summary>%Intl.NumberFormat%</summary>
    public JsFunction IntlNumberFormat => _intlNumberFormat!;

    /// <summary>%Intl%.[[FallbackSymbol]]: the symbol of the objects initialized by the legacy constructor mode of Intl.NumberFormat and Intl.DateTimeFormat.</summary>
    public JsSymbol IntlFallbackSymbol { get; } = new("IntlLegacyConstructedSymbol");

    /// <summary>Gets the NumberFormat of the default locale and options, used by Number.prototype.toLocaleString without arguments.</summary>
    public JsNumberFormatObject DefaultNumberFormat => _defaultNumberFormat ??= NumberFormatBuiltins.CreateDefault(this);

    partial void InitializeIntl()
    {
        _intl = IntlBuiltins.Initialize(this);
        _intlLocalePrototype = new JsObject(ObjectPrototype);
        _intlLocale = LocaleBuiltins.Initialize(this, _intl);
        _intlPluralRulesPrototype = new JsObject(ObjectPrototype);
        PluralRulesBuiltins.Initialize(this, _intl);
        _intlNumberFormatPrototype = new JsObject(ObjectPrototype);
        _intlNumberFormat = NumberFormatBuiltins.Initialize(this, _intl);
        InitializeIntlRelativeTimeFormat(_intl);
        InitializeIntlListFormat(_intl);
        InitializeIntlDisplayNames(_intl);
        LocaleSensitiveBuiltins.Initialize(this);
    }
}
