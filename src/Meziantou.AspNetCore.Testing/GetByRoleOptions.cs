namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures <see cref="LocatorScope.GetByRole(string, string?, GetByRoleOptions?)"/>: the states of the elements to find.</summary>
public sealed class GetByRoleOptions
{
    /// <summary>
    /// Gets whether the name matches the whole accessible name, with the case. By default, the name matches a substring of
    /// the accessible name, ignoring the case. The white space is collapsed in both cases.
    /// </summary>
    public bool Exact { get; init; }

    /// <summary>Gets the checked state of the elements (<c>aria-checked</c>, or the state of a check box), or null to match any state.</summary>
    public bool? Checked { get; init; }

    /// <summary>Gets the disabled state of the elements, or null to match any state.</summary>
    public bool? Disabled { get; init; }

    /// <summary>Gets the expanded state of the elements (<c>aria-expanded</c>), or null to match any state.</summary>
    public bool? Expanded { get; init; }

    /// <summary>Gets the pressed state of the toggle buttons (<c>aria-pressed</c>), or null to match any state.</summary>
    public bool? Pressed { get; init; }

    /// <summary>Gets the selected state of the elements (<c>aria-selected</c>, or the selectedness of an option), or null to match any state.</summary>
    public bool? Selected { get; init; }

    /// <summary>Gets the level of the headings, list items, rows or tree items (<c>aria-level</c>, or the level of an <c>h1</c>-<c>h6</c> element), or null to match any level.</summary>
    public int? Level { get; init; }

    /// <summary>
    /// Gets whether the elements that are hidden from the accessibility tree by <c>visibility: hidden</c> or
    /// <c>aria-hidden="true"</c> are found. The default is false. The role of an element that is not rendered
    /// (<c>display: none</c>) is <c>none</c>, so such an element is not found by its role: use a CSS locator.
    /// </summary>
    public bool IncludeHidden { get; init; }
}
