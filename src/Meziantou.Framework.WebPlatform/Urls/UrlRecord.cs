using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Urls;

internal enum UrlHostKind : byte
{
    Domain,
    IPv4,
    IPv6,
    Opaque,
    Empty,
}

/// <summary>A host (https://url.spec.whatwg.org/#concept-host). Stored in its serialized form.</summary>
internal readonly record struct UrlHost(UrlHostKind Kind, string Serialized)
{
    public static UrlHost EmptyHost { get; } = new(UrlHostKind.Empty, "");
}

/// <summary>A URL record: https://url.spec.whatwg.org/#concept-url</summary>
internal sealed class UrlRecord
{
    public string Scheme { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public UrlHost? Host { get; set; }

    /// <summary>The port, or -1 for null.</summary>
    public int Port { get; set; } = -1;

    /// <summary>The opaque path, or null when the path is a list.</summary>
    public string? OpaquePath { get; set; }

    public List<string> Path { get; set; } = [];

    public string? Query { get; set; }

    public string? Fragment { get; set; }

    public bool HasOpaquePath => OpaquePath is not null;

    public bool IsSpecial => UrlSchemes.IsSpecial(Scheme);

    public bool IncludesCredentials => Username.Length > 0 || Password.Length > 0;

    /// <summary>https://url.spec.whatwg.org/#cannot-have-a-username-password-port</summary>
    public bool CannotHaveUsernamePasswordPort => Host is null || Host.Value.Kind == UrlHostKind.Empty || Scheme == "file";

    public UrlRecord Clone()
    {
        return new UrlRecord
        {
            Scheme = Scheme,
            Username = Username,
            Password = Password,
            Host = Host,
            Port = Port,
            OpaquePath = OpaquePath,
            Path = [.. Path],
            Query = Query,
            Fragment = Fragment,
        };
    }

    /// <summary>https://url.spec.whatwg.org/#shorten-a-urls-path</summary>
    public void ShortenPath()
    {
        if (Scheme == "file" && Path.Count == 1 && UrlParser.IsNormalizedWindowsDriveLetter(Path[0]))
            return;

        if (Path.Count > 0)
        {
            Path.RemoveAt(Path.Count - 1);
        }
    }

    /// <summary>https://url.spec.whatwg.org/#concept-url-serializer</summary>
    public string Serialize(bool excludeFragment = false)
    {
        var output = StringBuilderCache.Acquire();
        output.Append(Scheme).Append(':');
        if (Host is { } host)
        {
            output.Append("//");
            if (IncludesCredentials)
            {
                output.Append(Username);
                if (Password.Length > 0)
                {
                    output.Append(':').Append(Password);
                }

                output.Append('@');
            }

            output.Append(host.Serialized);
            if (Port >= 0)
            {
                output.Append(':').Append(Port.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (Host is null && !HasOpaquePath && Path.Count > 1 && Path[0].Length == 0)
        {
            output.Append("/.");
        }

        AppendPath(output);
        if (Query is not null)
        {
            output.Append('?').Append(Query);
        }

        if (!excludeFragment && Fragment is not null)
        {
            output.Append('#').Append(Fragment);
        }

        return StringBuilderCache.GetStringAndRelease(output);
    }

    /// <summary>https://url.spec.whatwg.org/#url-path-serializer</summary>
    public string SerializePath()
    {
        if (OpaquePath is not null)
            return OpaquePath;

        if (Path.Count == 1)
            return "/" + Path[0];

        var output = StringBuilderCache.Acquire();
        AppendPath(output);
        return StringBuilderCache.GetStringAndRelease(output);
    }

    private void AppendPath(StringBuilder output)
    {
        if (OpaquePath is not null)
        {
            output.Append(OpaquePath);
            return;
        }

        foreach (var segment in Path)
        {
            output.Append('/').Append(segment);
        }
    }

    /// <summary>
    /// Serialization of the origin of the URL (https://url.spec.whatwg.org/#concept-url-origin and
    /// https://html.spec.whatwg.org/#ascii-serialisation-of-an-origin).
    /// </summary>
    public string SerializeOrigin()
    {
        switch (Scheme)
        {
            case "blob":
            {
                // The origin of the path URL, which is the origin of the document that created a blob URL (https://w3c.github.io/FileAPI/#unicodeBlobURL)
                var pathUrl = UrlParser.Parse(SerializePath(), baseUrl: null);
                if (pathUrl is not null && pathUrl.Scheme is "http" or "https" or "file")
                    return pathUrl.SerializeOrigin();

                return "null";
            }

            case "ftp" or "http" or "https" or "ws" or "wss":
            {
                var result = Scheme + "://" + Host!.Value.Serialized;
                if (Port >= 0)
                {
                    result += ":" + Port.ToString(CultureInfo.InvariantCulture);
                }

                return result;
            }

            default:
                return "null";
        }
    }
}

internal static class UrlSchemes
{
    /// <summary>https://url.spec.whatwg.org/#special-scheme</summary>
    public static bool IsSpecial(string scheme) => scheme is "ftp" or "file" or "http" or "https" or "ws" or "wss";

    /// <summary>https://url.spec.whatwg.org/#default-port. Returns -1 for null.</summary>
    public static int GetDefaultPort(string scheme) => scheme switch
    {
        "ftp" => 21,
        "http" or "ws" => 80,
        "https" or "wss" => 443,
        _ => -1,
    };
}
