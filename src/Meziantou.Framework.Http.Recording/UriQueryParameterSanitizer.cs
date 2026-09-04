namespace Meziantou.Framework.Http.Recording;

/// <summary>Replaces the value of the specified query string parameters in the recorded request URI.</summary>
/// <remarks>
/// <see cref="HeaderRemovalSanitizer"/> only reaches headers. Secrets frequently travel in the query string
/// (<c>api_key</c>, <c>sig</c>, <c>token</c>), which this sanitizer redacts. Credentials in the userinfo component
/// (<c>https://user:password@host/</c>) are removed unconditionally when the request is captured.
/// </remarks>
public sealed class UriQueryParameterSanitizer : IHttpRecordingSanitizer
{
    private readonly HashSet<string> _parameterNames;

    public UriQueryParameterSanitizer(params string[] parameterNames)
    {
        ArgumentNullException.ThrowIfNull(parameterNames);
        _parameterNames = new HashSet<string>(parameterNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Sanitize(HttpRecordingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_parameterNames.Count is 0)
            return;

        entry.RequestUri = HttpRecordingUri.MaskQueryParameters(entry.RequestUri, _parameterNames);
    }
}
