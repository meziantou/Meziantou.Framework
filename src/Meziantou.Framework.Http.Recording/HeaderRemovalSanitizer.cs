namespace Meziantou.Framework.Http.Recording;

/// <summary>Removes specified headers from recorded entries before persistence.</summary>
public sealed class HeaderRemovalSanitizer : IHttpRecordingSanitizer
{
    private readonly HashSet<string> _headerNames;

    public HeaderRemovalSanitizer(params string[] headerNames)
    {
        _headerNames = new HashSet<string>(headerNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Sanitize(HttpRecordingEntry entry)
    {
        RemoveHeaders(entry.RequestHeaders);
        RemoveHeaders(entry.ResponseHeaders);
    }

    private void RemoveHeaders(Dictionary<string, string[]>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var name in _headerNames)
        {
            headers.Remove(name);
        }
    }
}
