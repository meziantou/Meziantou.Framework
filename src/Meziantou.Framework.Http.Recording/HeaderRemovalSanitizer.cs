namespace Meziantou.Framework.Http.Recording;

/// <summary>Removes the specified headers from recorded entries.</summary>
/// <remarks>
/// This sanitizer only reaches headers. Secrets carried in the request URI or in a body are not affected:
/// use <see cref="UriQueryParameterSanitizer"/> for query string parameters, or a custom
/// <see cref="IHttpRecordingSanitizer"/> to rewrite <see cref="HttpRecordingEntry.RequestBody"/> and
/// <see cref="HttpRecordingEntry.ResponseBody"/>.
/// </remarks>
public sealed class HeaderRemovalSanitizer : IHttpRecordingSanitizer
{
    private readonly HashSet<string> _headerNames;

    public HeaderRemovalSanitizer(params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        _headerNames = new HashSet<string>(headerNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Sanitize(HttpRecordingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RemoveHeaders(entry.RequestHeaders);
        RemoveHeaders(entry.ResponseHeaders);
    }

    private void RemoveHeaders(Dictionary<string, string[]>? headers)
    {
        if (headers is null)
            return;

        foreach (var name in _headerNames)
        {
            headers.Remove(name);
        }
    }
}
