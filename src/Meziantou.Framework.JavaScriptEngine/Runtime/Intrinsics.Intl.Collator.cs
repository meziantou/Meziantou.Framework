using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _intlCollatorPrototype;
    private JsFunction? _intlCollator;
    private JsCollatorObject? _defaultCollator;
    private Dictionary<string, JsCollatorObject>? _localeCompareCollators;

    /// <summary>%Intl.Collator.prototype%</summary>
    public JsObject IntlCollatorPrototype => _intlCollatorPrototype!;

    /// <summary>%Intl.Collator%</summary>
    public JsFunction IntlCollator => _intlCollator!;

    /// <summary>Gets the Collator of the default locale and options, used by String.prototype.localeCompare without arguments.</summary>
    public JsCollatorObject DefaultCollator => _defaultCollator ??= CollatorBuiltins.CreateDefault(this);

    /// <summary>Gets the Collator of a locale with the default options, used by String.prototype.localeCompare(that, locales).</summary>
    public JsCollatorObject GetLocaleCompareCollator(string locales)
    {
        var cache = _localeCompareCollators ??= new Dictionary<string, JsCollatorObject>(StringComparer.Ordinal);
        if (!cache.TryGetValue(locales, out var collator))
        {
            collator = new JsCollatorObject(IntlCollatorPrototype);
            CollatorBuiltins.Initialize(collator, locales, default);
            if (cache.Count >= 32)
            {
                cache.Clear();
            }

            cache.Add(locales, collator);
        }

        return collator;
    }

    private void InitializeIntlCollator(JsObject intl)
    {
        _intlCollatorPrototype = new JsObject(ObjectPrototype);
        _intlCollator = CollatorBuiltins.Initialize(this, intl);
    }
}
