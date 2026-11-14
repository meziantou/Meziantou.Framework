using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>A step of a locator: a search of the elements of the scope, or a filter of the elements of the previous step.</summary>
internal abstract class LocatorStep
{
    /// <summary>Applies the step.</summary>
    /// <param name="evaluator">The evaluation.</param>
    /// <param name="initialScope">The scope of the first step (<see cref="Locator.And"/> and <see cref="Locator.Or"/> evaluate the other locator from it).</param>
    /// <param name="scope">The scope that the step searches.</param>
    /// <param name="elements">The elements of the previous step.</param>
    /// <returns>The elements, in document order.</returns>
    public abstract List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements);

    /// <summary>Gets the description of the step, as C# code (<c>GetByRole("button", "Save")</c>).</summary>
    public abstract override string ToString();
}
