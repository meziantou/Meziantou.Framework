namespace Meziantou.Framework.HumanReadable.Converters;

public sealed record HumanReadableHttpOptions
{
    public HumanReadableHttpRequestMessageOptions RequestMessageOptions { get; set; } = new();
    public HumanReadableHttpResponseMessageOptions ResponseMessageOptions { get; set; } = new();
}
