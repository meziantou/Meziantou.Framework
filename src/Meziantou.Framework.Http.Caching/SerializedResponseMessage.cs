using System.Net;

namespace HttpCaching;

internal sealed class SerializedResponseMessage
{
    public HttpStatusCode HttpStatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public IEnumerable<KeyValuePair<string, string[]>>? Headers { get; set; }
    public IEnumerable<KeyValuePair<string, string[]>>? ContentHeaders { get; set; }
    public IEnumerable<KeyValuePair<string, string[]>>? TrailingHeaders { get; set; }
    public byte[]? Content { get; set; }
}
