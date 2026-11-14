using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Combines a locator with another one, evaluated from the same scope: the elements that both find, or that either finds.</summary>
internal sealed class CombineStep(Locator other, bool union) : LocatorStep
{
    public override List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements)
    {
        var others = evaluator.Evaluate(other.Steps, initialScope);
        if (union)
            return evaluator.SortInDocumentOrder(elements.Concat(others));

        var set = others.ToHashSet();
        return [.. elements.Where(set.Contains)];
    }

    public override string ToString() => (union ? "Or(" : "And(") + other + ")";
}
