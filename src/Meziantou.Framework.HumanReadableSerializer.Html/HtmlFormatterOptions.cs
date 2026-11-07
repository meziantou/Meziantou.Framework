namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Provides options for formatting HTML.</summary>
public sealed record HtmlFormatterOptions
{
    /// <summary>Gets or sets the quote style to use for attribute values.</summary>
    public HtmlAttributeQuote? AttributeQuote { get; set; }

    /// <summary>Gets or sets whether to order attributes alphabetically.</summary>
    public bool OrderAttributes { get; set; }

    /// <summary>Gets or sets whether to redact Content Security Policy nonce values.</summary>
    public bool RedactContentSecurityPolicyNonce { get; set; }
}
