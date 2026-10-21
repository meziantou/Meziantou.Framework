using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlDateTimeFormatPrototype;
    private JsFunction? _intlDateTimeFormat;

    /// <summary>%Intl.DateTimeFormat.prototype%</summary>
    public JsObject IntlDateTimeFormatPrototype => EnsureIntl()._intlDateTimeFormatPrototype!;

    /// <summary>%Intl.DateTimeFormat%</summary>
    public JsFunction IntlDateTimeFormat => EnsureIntl()._intlDateTimeFormat!;

    /// <summary>Gets the cached DateTimeFormats of the default locale and options used by the toLocale*String methods without arguments.</summary>
    internal JsDateTimeFormatObject?[] DefaultDateTimeFormats { get; } = new JsDateTimeFormatObject?[3];

    private void InitializeIntlDateTimeFormat(JsObject intl)
    {
        _intlDateTimeFormatPrototype = new JsObject(ObjectPrototype);
        _intlDateTimeFormat = DateTimeFormatBuiltins.Initialize(this, intl);
    }
}
