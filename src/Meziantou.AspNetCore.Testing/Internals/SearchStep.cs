using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>A step that searches the elements of its scope.</summary>
internal abstract class SearchStep : LocatorStep
{
    public sealed override List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements)
    {
        var result = new List<Element>();
        foreach (var candidate in LocatorEvaluator.GetCandidates(scope))
        {
            if (IsMatch(evaluator, candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    protected abstract bool IsMatch(LocatorEvaluator evaluator, Element element);
}
