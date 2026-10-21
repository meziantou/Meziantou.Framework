using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlDurationFormatPrototype;

    /// <summary>%Intl.DurationFormat.prototype%</summary>
    public JsObject IntlDurationFormatPrototype => _intlDurationFormatPrototype!;

    /// <summary>%Intl.DurationFormat%</summary>
    public JsFunction IntlDurationFormat { get; private set; } = null!;

    private void InitializeIntlDurationFormat(JsObject intl)
    {
        _intlDurationFormatPrototype = new JsObject(ObjectPrototype);
        IntlDurationFormat = DurationFormatBuiltins.Initialize(this, intl, _intlDurationFormatPrototype);
    }
}
