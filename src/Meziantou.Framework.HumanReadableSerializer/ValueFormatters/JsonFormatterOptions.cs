namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public sealed record JsonFormatterOptions
{
    public bool FormatAsStandardObject { get; set; }

    public bool WriteIndented { get; set; } = true;

    public bool OrderProperties { get; set; }
}
