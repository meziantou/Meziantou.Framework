namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public sealed record UrlEncodedFormFormatterOptions
{
    public bool OrderProperties { get; set; } = true;
    public bool UnescapeValues { get; set; } = true;
    public bool PrettyFormat { get; set; } = true;
}
