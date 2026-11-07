using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>A request received by an emulated handler of the WPT server (the <c>request</c> object of wptserve).</summary>
internal sealed class WptHandlerRequest
{
    private List<KeyValuePair<string, string>>? _query;

    /// <summary>Creates the request of a handler.</summary>
    /// <param name="url">The URL of the request.</param>
    /// <param name="request">The request of the library.</param>
    /// <param name="headers">The headers sent by the browser (see <c>WptTestRunner.GetRequestHeaders</c>).</param>
    public WptHandlerRequest(Url url, DocumentRequest request, IEnumerable<KeyValuePair<string, string>> headers)
    {
        Url = url;
        Request = request;
        Method = request.Method;
        Body = request.Body?.ToArray() ?? [];
        Headers = [.. headers];
    }

    public Url Url { get; }

    /// <summary>The time at which the request is received, measured by the virtual clock (<c>time.time()</c>).</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>The time elapsed since the request was received, when the handler runs again (see <see cref="WptHandlerResponse.RetryAfter"/>), measured by the virtual clock.</summary>
    public TimeSpan PollingElapsed { get; init; }

    /// <summary>The request of the library (its destination, its mode...).</summary>
    public DocumentRequest Request { get; }

    public string Method { get; }

    /// <summary>The headers of the request, as byte strings.</summary>
    public List<KeyValuePair<string, string>> Headers { get; }

    public byte[] Body { get; }

    /// <summary>The path of the URL (<c>request.url_parts.path</c>).</summary>
    public string Path => Url.Pathname;

    /// <summary>The query of the URL, without the <c>?</c> (<c>request.url_parts.query</c>).</summary>
    public string Query => Url.Search.Length > 0 ? Url.Search[1..] : "";

    /// <summary>The parameters of the query, decoded as byte strings (<c>request.GET</c>: parse_qsl with ISO-8859-1).</summary>
    public List<KeyValuePair<string, string>> GetParameters => _query ??= ParseQuery(Query);

    public bool Has(string name) => GetParameters.Exists(parameter => parameter.Key == name);

    /// <summary><c>request.GET.first(name, default)</c></summary>
    public string? Get(string name, string? defaultValue = null) => GetParameters.Find(parameter => parameter.Key == name) is { Key: not null } parameter ? parameter.Value : defaultValue;

    /// <summary><c>request.headers.get(name)</c>: the values of the headers with the name, separated by <c>", "</c>, or null.</summary>
    public string? Header(string name) => HeaderList.Get(Headers, name);

    public bool HasHeader(string name) => Header(name) is not null;

    /// <summary>The credentials of the <c>Authorization</c> header (<c>request.auth</c>).</summary>
    public (string? Username, string? Password) Auth
    {
        get
        {
            if (Header("Authorization") is not { } authorization || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return (null, null);

            try
            {
                var credentials = Encoding.Latin1.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                var index = credentials.IndexOf(':', StringComparison.Ordinal);
                return index < 0 ? (credentials, "") : (credentials[..index], credentials[(index + 1)..]);
            }
            catch (FormatException)
            {
                return (null, null);
            }
        }
    }

    /// <summary>The fields of a form submitted with the POST method (<c>request.POST</c>), or an empty list.</summary>
    public List<KeyValuePair<string, FormDataEntryValue>> Post
    {
        get
        {
            try
            {
                return [.. FetchBodyParsing.ParseFormData(Body, Header("Content-Type"))];
            }
            catch (TypeErrorException)
            {
                return [];
            }
        }
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var part in query.Split('&'))
        {
            if (part.Length == 0)
                continue;

            var index = part.IndexOf('=', StringComparison.Ordinal);
            var name = index < 0 ? part : part[..index];
            var value = index < 0 ? "" : part[(index + 1)..];
            result.Add(new(Decode(name), Decode(value)));
        }

        return result;

        static string Decode(string value) => Encoding.Latin1.GetString(PercentEncoding.PercentDecode(value.Replace('+', ' ')));
    }
}
