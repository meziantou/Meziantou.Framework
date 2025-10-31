namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Provides options for formatting XML.</summary>
public sealed record XmlFormatterOptions
{
    /// <summary>Gets or sets whether to write indented XML.</summary>
    public bool WriteIndented { get; set; } = true;

    /// <summary>Gets or sets whether to order attributes alphabetically.</summary>
    public bool OrderAttributes { get; set; }
}
