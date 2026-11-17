using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures <see cref="Locator.Filter"/>: the conditions that the elements of a locator must satisfy.</summary>
public sealed class LocatorFilterOptions
{
    /// <summary>Gets a text that the elements contain, somewhere in their descendants (a substring, ignoring the case and collapsing the white space).</summary>
    public string? HasText { get; init; }

    /// <summary>Gets a regular expression that matches the text of the elements.</summary>
    public Regex? HasTextRegex { get; init; }

    /// <summary>Gets a text that the elements do not contain (a substring, ignoring the case and collapsing the white space).</summary>
    public string? HasNotText { get; init; }

    /// <summary>Gets a regular expression that does not match the text of the elements.</summary>
    public Regex? HasNotTextRegex { get; init; }

    /// <summary>
    /// Gets a locator that finds at least one element inside the elements, evaluated from each element
    /// (<c>page.GetByRole("listitem").Filter(new() { Has = page.GetByRole("button", "Delete") })</c>).
    /// </summary>
    public Locator? Has { get; init; }

    /// <summary>Gets a locator that finds no element inside the elements, evaluated from each element.</summary>
    public Locator? HasNot { get; init; }

    /// <summary>Gets whether the elements are visible, or null to keep the visible and the hidden elements.</summary>
    public bool? Visible { get; init; }
}
