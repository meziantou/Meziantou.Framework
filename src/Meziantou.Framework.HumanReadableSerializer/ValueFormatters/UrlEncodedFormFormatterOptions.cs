namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Provides options for formatting URL-encoded form data.</summary>
public sealed record UrlEncodedFormFormatterOptions
{
    /// <summary>Gets or sets whether to order properties alphabetically.</summary>
    public bool OrderProperties { get; set; } = true;

    /// <summary>Gets or sets whether to unescape URL-encoded values.</summary>
    public bool UnescapeValues { get; set; } = true;

    /// <summary>Gets or sets whether to format the output in a pretty, human-readable format.</summary>
    public bool PrettyFormat { get; set; } = true;
}
