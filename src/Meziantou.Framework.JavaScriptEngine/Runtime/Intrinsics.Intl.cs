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
    public JsObject Intl => EnsureIntl()._intl!;

    /// <summary>%Intl.Locale.prototype%</summary>
    public JsObject IntlLocalePrototype => EnsureIntl()._intlLocalePrototype!;

    /// <summary>%Intl.Locale%</summary>
    public JsFunction IntlLocale => EnsureIntl()._intlLocale!;

    /// <summary>%Intl.PluralRules.prototype%</summary>
    public JsObject IntlPluralRulesPrototype => EnsureIntl()._intlPluralRulesPrototype!;

    /// <summary>%Intl.NumberFormat.prototype%</summary>
    public JsObject IntlNumberFormatPrototype => EnsureIntl()._intlNumberFormatPrototype!;

    /// <summary>%Intl.NumberFormat%</summary>
    public JsFunction IntlNumberFormat => EnsureIntl()._intlNumberFormat!;

    /// <summary>%Intl%.[[FallbackSymbol]]: the symbol of the objects initialized by the legacy constructor mode of Intl.NumberFormat and Intl.DateTimeFormat.</summary>
    public JsSymbol IntlFallbackSymbol { get; } = new("IntlLegacyConstructedSymbol");

    /// <summary>Gets the NumberFormat of the default locale and options, used by Number.prototype.toLocaleString without arguments.</summary>
    public JsNumberFormatObject DefaultNumberFormat => _defaultNumberFormat ??= NumberFormatBuiltins.CreateDefault(this);

    // The Intl object and its constructors are created when the global Intl is read or a locale-sensitive method needs
    // them (most scripts do not use them: the creation of a realm is cheaper); the locale-sensitive methods of the other
    // built-ins are defined with the realm
    private bool _isIntlInitializing;

    partial void InitializeIntl()
    {
        AddLazyGlobal("Intl", static intrinsics => intrinsics.EnsureIntl()._intl!);
        CollatorBuiltins.InitializeLocaleCompare(this);
        DateTimeFormatLocaleSensitiveBuiltins.Initialize(this);
        LocaleSensitiveBuiltins.Initialize(this);
    }

    /// <summary>Creates the Intl object and its constructors, if they are not created yet.</summary>
    private Intrinsics EnsureIntl()
    {
        if (_intl is null && !_isIntlInitializing)
        {
            _isIntlInitializing = true;
            InitializeIntlObjects();
        }

        return this;
    }

    private void InitializeIntlObjects()
    {
        _intl = IntlBuiltins.Initialize(this);
        _intlLocalePrototype = new JsObject(ObjectPrototype);
        _intlLocale = LocaleBuiltins.Initialize(this, _intl);
        _intlPluralRulesPrototype = new JsObject(ObjectPrototype);
        PluralRulesBuiltins.Initialize(this, _intl);
        _intlNumberFormatPrototype = new JsObject(ObjectPrototype);
        _intlNumberFormat = NumberFormatBuiltins.Initialize(this, _intl);
        InitializeIntlCollator(_intl);
        InitializeIntlDateTimeFormat(_intl);
        InitializeIntlRelativeTimeFormat(_intl);
        InitializeIntlListFormat(_intl);
        InitializeIntlDisplayNames(_intl);
        InitializeIntlSegmenter(_intl);
        InitializeIntlDurationFormat(_intl);
    }
}
