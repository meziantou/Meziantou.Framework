using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlSegmenterPrototype;
    private JsObject? _intlSegmentsPrototype;
    private JsObject? _intlSegmentIteratorPrototype;

    /// <summary>%Intl.Segmenter.prototype%</summary>
    public JsObject IntlSegmenterPrototype => _intlSegmenterPrototype!;

    /// <summary>%IntlSegmentsPrototype%</summary>
    public JsObject IntlSegmentsPrototype => _intlSegmentsPrototype!;

    /// <summary>%IntlSegmentIteratorPrototype%</summary>
    public JsObject IntlSegmentIteratorPrototype => _intlSegmentIteratorPrototype!;

    private void InitializeIntlSegmenter(JsObject intl)
    {
        _intlSegmenterPrototype = new JsObject(ObjectPrototype);
        _intlSegmentsPrototype = new JsObject(ObjectPrototype);
        _intlSegmentIteratorPrototype = new JsObject(IteratorPrototype);
        SegmenterBuiltins.Initialize(this, intl, _intlSegmenterPrototype, _intlSegmentsPrototype, _intlSegmentIteratorPrototype);
    }
}
