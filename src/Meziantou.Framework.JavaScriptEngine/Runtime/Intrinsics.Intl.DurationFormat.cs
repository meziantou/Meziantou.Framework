using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlDurationFormatPrototype;

    /// <summary>%Intl.DurationFormat.prototype%</summary>
    public JsObject IntlDurationFormatPrototype => _intlDurationFormatPrototype!;

    private void InitializeIntlDurationFormat(JsObject intl)
    {
        _intlDurationFormatPrototype = new JsObject(ObjectPrototype);
        DurationFormatBuiltins.Initialize(this, intl, _intlDurationFormatPrototype);
    }
}
