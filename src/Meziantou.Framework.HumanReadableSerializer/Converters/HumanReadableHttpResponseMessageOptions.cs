namespace Meziantou.Framework.HumanReadable.Converters;

public sealed record HumanReadableHttpResponseMessageOptions : HumanReadableHttpMessageOptions
{
    public HumanReadableHttpResponseMessageOptions()
        : base()
    {
        ExcludedHeaderNames.Add("Date");
        ExcludedHeaderNames.Add("Alt-Svc");
    }

    public bool RedactContentSecurityPolicyNonce { get; set; }
    public HttpRequestMessageFormat RequestMessageFormat { get; set; }
}
