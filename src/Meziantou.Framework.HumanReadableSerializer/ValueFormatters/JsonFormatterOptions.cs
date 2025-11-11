namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Provides options for formatting JSON.</summary>
public sealed record JsonFormatterOptions
{
    /// <summary>Gets or sets whether to format JSON as a standard object in the output.</summary>
    public bool FormatAsStandardObject { get; set; }

    /// <summary>Gets or sets whether to write indented JSON.</summary>
    public bool WriteIndented { get; set; } = true;

    /// <summary>Gets or sets whether to order properties alphabetically.</summary>
    public bool OrderProperties { get; set; }
}
