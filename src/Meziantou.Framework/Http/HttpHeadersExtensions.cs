using System.Net.Http.Headers;

namespace Meziantou.Framework.Http;

public static class HttpHeadersExtensions
{
    public static IEnumerable<LinkHeaderValue> ParseLinkHeaders(this HttpHeaders headers)
    {
        if (headers.TryGetValues("link", out var values))
        {
            foreach (var value in values)
            {
                foreach (var linkValue in LinkHeaderValue.Parse(value))
                {
                    yield return linkValue;
                }
            }
        }
    }
}
