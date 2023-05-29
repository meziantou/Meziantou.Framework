namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public sealed record HtmlFormatterOptions
{
    public HtmlAttributeQuote? AttributeQuote { get; set; }
    public bool OrderAttributes { get; set; }
    public bool RedactContentSecurityPolicyNonce { get; set; }
}
