using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class NthStep(int index) : LocatorStep
{
    public override List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements)
    {
        var actualIndex = index < 0 ? elements.Count + index : index;
        return actualIndex >= 0 && actualIndex < elements.Count ? [elements[actualIndex]] : [];
    }

    public override string ToString() => index switch
    {
        0 => "First",
        -1 => "Last",
        _ => "Nth(" + TextMatcher.Format(index) + ")",
    };
}
