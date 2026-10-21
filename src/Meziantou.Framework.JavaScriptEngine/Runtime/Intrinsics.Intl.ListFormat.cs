using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlListFormatPrototype;

    /// <summary>%Intl.ListFormat.prototype%</summary>
    public JsObject IntlListFormatPrototype => EnsureIntl()._intlListFormatPrototype!;

    private void InitializeIntlListFormat(JsObject intl)
    {
        _intlListFormatPrototype = new JsObject(ObjectPrototype);
        ListFormatBuiltins.Initialize(this, intl, _intlListFormatPrototype);
    }
}
