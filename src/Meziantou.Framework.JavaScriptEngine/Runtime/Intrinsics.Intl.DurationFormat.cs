using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlDurationFormatPrototype;
    private JsFunction? _intlDurationFormat;

    /// <summary>%Intl.DurationFormat.prototype%</summary>
    public JsObject IntlDurationFormatPrototype => EnsureIntl()._intlDurationFormatPrototype!;

    /// <summary>%Intl.DurationFormat%</summary>
    public JsFunction IntlDurationFormat => EnsureIntl()._intlDurationFormat!;

    private void InitializeIntlDurationFormat(JsObject intl)
    {
        _intlDurationFormatPrototype = new JsObject(ObjectPrototype);
        _intlDurationFormat = DurationFormatBuiltins.Initialize(this, intl, _intlDurationFormatPrototype);
    }
}
