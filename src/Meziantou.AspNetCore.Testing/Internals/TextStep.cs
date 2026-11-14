using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Finds the smallest elements whose text matches: an element matches when none of its children matches.</summary>
internal sealed class TextStep(TextMatcher text) : SearchStep
{
    protected override bool IsMatch(LocatorEvaluator evaluator, Element element)
    {
        if (LocatorEvaluator.IsSkippedForText(element) || IsInHead(element) || !text.IsMatch(evaluator.GetText(element)))
            return false;

        foreach (var child in ComposedTree.GetChildElements(element))
        {
            if (!LocatorEvaluator.IsSkippedForText(child) && text.IsMatch(evaluator.GetText(child)))
                return false;
        }

        return true;
    }

    private static bool IsInHead(Element element) => element.OwnerDocument?.Head is { } head && head.Contains(element);

    public override string ToString() => "GetByText(" + text + ")";
}
