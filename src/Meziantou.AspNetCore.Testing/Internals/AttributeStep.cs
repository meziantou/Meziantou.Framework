using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class AttributeStep(string methodName, string attributeName, TextMatcher value) : SearchStep
{
    protected override bool IsMatch(LocatorEvaluator evaluator, Element element) => element.GetAttribute(attributeName) is { } attribute && value.IsMatch(attribute);

    public override string ToString() => methodName + "(" + value + ")";
}
