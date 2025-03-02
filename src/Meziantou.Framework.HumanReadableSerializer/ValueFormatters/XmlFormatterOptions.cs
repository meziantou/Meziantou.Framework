namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public sealed record XmlFormatterOptions
{
    public bool WriteIndented { get; set; } = true;

    public bool OrderAttributes { get; set; }
}
