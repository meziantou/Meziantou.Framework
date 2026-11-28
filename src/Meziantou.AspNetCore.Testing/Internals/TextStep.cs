using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Finds the smallest elements whose text matches: an element matches when none of its children matches.</summary>
internal sealed class TextStep(TextMatcher text) : SearchStep
{
    protected override bool IsMatch(LocatorEvaluator evaluator, Element element)
    {
        if (LocatorEvaluator.IsSkippedForText(element) || IsInHead(element) || IsInSkippedElement(element) || !text.IsMatch(evaluator, element))
            return false;

        foreach (var child in ComposedTree.GetChildElements(element))
        {
            if (!LocatorEvaluator.IsSkippedForText(child) && text.IsMatch(evaluator, child))
                return false;
        }

        return true;
    }

    private static bool IsInHead(Element element) => element.OwnerDocument?.Head is { } head && head.Contains(element);

    // The text of an element leaves out the content of its script, style, noscript and template elements: the elements in it
    // are not found either (a noscript parsed without scripting has elements), or they would be found with an ancestor whose
    // text matches without them
    private static bool IsInSkippedElement(Element element)
    {
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (LocatorEvaluator.IsSkippedForText(parent))
                return true;
        }

        return false;
    }

    public override string ToString() => "GetByText(" + text + ")";
}
