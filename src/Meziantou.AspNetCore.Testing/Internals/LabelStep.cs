using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class LabelStep(TextMatcher label) : SearchStep
{
    protected override bool IsMatch(LocatorEvaluator evaluator, Element element)
    {
        foreach (var text in evaluator.GetLabels(element))
        {
            if (label.IsMatch(text))
                return true;
        }

        return false;
    }

    public override string ToString() => "GetByLabel(" + label + ")";
}
