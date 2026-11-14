using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class FilterStep(LocatorFilterOptions options) : LocatorStep
{
    private readonly TextMatcher? _hasText = options.HasTextRegex is { } regex ? TextMatcher.Create(regex) : options.HasText is { } text ? TextMatcher.Create(text, exact: false) : null;
    private readonly TextMatcher? _hasNotText = options.HasNotTextRegex is { } regex ? TextMatcher.Create(regex) : options.HasNotText is { } text ? TextMatcher.Create(text, exact: false) : null;

    public override List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements)
    {
        var result = new List<Element>();
        foreach (var element in elements)
        {
            if (_hasText is not null && !_hasText.IsMatch(evaluator.GetText(element)))
                continue;

            if (_hasNotText is not null && _hasNotText.IsMatch(evaluator.GetText(element)))
                continue;

            if (options.Has is { } has && evaluator.Evaluate(has.Steps, new SearchScope([element], IncludeNodes: false)).Count == 0)
                continue;

            if (options.HasNot is { } hasNot && evaluator.Evaluate(hasNot.Steps, new SearchScope([element], IncludeNodes: false)).Count > 0)
                continue;

            if (options.Visible is { } visible && ElementStates.IsVisible(element) != visible)
                continue;

            result.Add(element);
        }

        return result;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (_hasText is not null)
        {
            parts.Add("HasText = " + _hasText);
        }

        if (_hasNotText is not null)
        {
            parts.Add("HasNotText = " + _hasNotText);
        }

        if (options.Has is { } has)
        {
            parts.Add("Has = " + has);
        }

        if (options.HasNot is { } hasNot)
        {
            parts.Add("HasNot = " + hasNot);
        }

        if (options.Visible is { } visible)
        {
            parts.Add("Visible = " + (visible ? "true" : "false"));
        }

        return "Filter(" + string.Join(", ", parts) + ")";
    }
}
