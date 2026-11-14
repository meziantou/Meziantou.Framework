namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures the HTML snapshots of the DOM.</summary>
public sealed class MarkupOptions
{
    /// <summary>Gets the default options.</summary>
    public static MarkupOptions Default { get; } = new();

    /// <summary>Gets whether the HTML is indented, one element per line. The default is true.</summary>
    public bool Indent { get; init; } = true;

    /// <summary>
    /// Gets whether the state of the form fields is written as attributes: the value of the text fields (<c>value</c>), the
    /// state of the check boxes and radio buttons (<c>checked</c>) and the selected options (<c>selected</c>). Blazor sets
    /// these properties, not the attributes, so they are not in the DOM otherwise. The default is true.
    /// </summary>
    public bool IncludeFormState { get; init; } = true;

    /// <summary>Gets whether the <c>_bl_*</c> attributes, which identify the elements of the element references, are removed. The default is true.</summary>
    public bool RemoveElementReferenceAttributes { get; init; } = true;

    /// <summary>Gets whether the <c>b-*</c> attributes of the CSS isolation are removed. The default is false.</summary>
    public bool RemoveScopedCssAttributes { get; init; }
}
