using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>The target of an action: an element, or a locator that finds its element again before each attempt.</summary>
internal abstract class ActionTarget
{
    public static ActionTarget From(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new ElementTarget(element);
    }

    public static ActionTarget From(Locator locator) => new LocatorTarget(locator);

    /// <summary>Gets the element, or null when there is none yet.</summary>
    /// <param name="reason">Why there is no element.</param>
    /// <returns>The element, or null.</returns>
    /// <exception cref="StrictModeViolationException">The locator finds more than one element.</exception>
    public abstract Element? Resolve(out string? reason);

    public abstract override string ToString();

    private sealed class ElementTarget(Element element) : ActionTarget
    {
        public override Element? Resolve(out string? reason)
        {
            if (element.IsConnected)
            {
                reason = null;
                return element;
            }

            reason = "the element is not in the document";
            return null;
        }

        public override string ToString() => DescribeElement(element);
    }

    private sealed class LocatorTarget(Locator locator) : ActionTarget
    {
        public override Element? Resolve(out string? reason)
        {
            var element = locator.ResolveSingle();
            reason = element is null ? "no element matches the locator" : null;
            return element;
        }

        public override string ToString() => locator.ToString();
    }

    /// <summary>Describes an element for the messages: its name, its identifier and its classes.</summary>
    public static string DescribeElement(Element element)
    {
        var description = "<" + element.LocalName;
        if (element.Id is { Length: > 0 } id)
        {
            description += " id=\"" + id + "\"";
        }

        if (element.GetAttribute("class") is { Length: > 0 } className)
        {
            description += " class=\"" + className + "\"";
        }

        return description + ">";
    }
}
