using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlDisplayNamesPrototype;

    /// <summary>%Intl.DisplayNames.prototype%</summary>
    public JsObject IntlDisplayNamesPrototype => EnsureIntl()._intlDisplayNamesPrototype!;

    private void InitializeIntlDisplayNames(JsObject intl)
    {
        _intlDisplayNamesPrototype = new JsObject(ObjectPrototype);
        DisplayNamesBuiltins.Initialize(this, intl, _intlDisplayNamesPrototype);
    }
}
