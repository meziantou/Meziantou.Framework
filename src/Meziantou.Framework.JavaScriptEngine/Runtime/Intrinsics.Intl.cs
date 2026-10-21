using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intl;
    private JsObject? _intlLocalePrototype;
    private JsFunction? _intlLocale;

    /// <summary>%Intl%</summary>
    public JsObject Intl => _intl!;

    /// <summary>%Intl.Locale.prototype%</summary>
    public JsObject IntlLocalePrototype => _intlLocalePrototype!;

    /// <summary>%Intl.Locale%</summary>
    public JsFunction IntlLocale => _intlLocale!;

    partial void InitializeIntl()
    {
        _intl = IntlBuiltins.Initialize(this);
        _intlLocalePrototype = new JsObject(ObjectPrototype);
        _intlLocale = LocaleBuiltins.Initialize(this, _intl);
    }
}
