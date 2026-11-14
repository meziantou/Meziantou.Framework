using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class CssStep(string selector) : SearchStep
{
    private readonly SelectorList _selector = SelectorList.Parse(selector);

    protected override bool IsMatch(LocatorEvaluator evaluator, Element element) => element.Matches(_selector);

    public override string ToString() => "Locator(" + TextMatcher.Quote(selector) + ")";
}
