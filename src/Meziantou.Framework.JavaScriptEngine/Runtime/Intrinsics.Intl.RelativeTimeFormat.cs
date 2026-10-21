using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlRelativeTimeFormatPrototype;

    /// <summary>%Intl.RelativeTimeFormat.prototype%</summary>
    public JsObject IntlRelativeTimeFormatPrototype => _intlRelativeTimeFormatPrototype!;

    private void InitializeIntlRelativeTimeFormat(JsObject intl)
    {
        _intlRelativeTimeFormatPrototype = new JsObject(ObjectPrototype);
        RelativeTimeFormatBuiltins.Initialize(this, intl, _intlRelativeTimeFormatPrototype);
    }
}
