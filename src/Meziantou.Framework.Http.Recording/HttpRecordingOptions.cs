namespace Meziantou.Framework.Http.Recording;

/// <summary>Configuration options for the HTTP recording handler.</summary>
public sealed class HttpRecordingOptions
{
    /// <summary>Gets or sets the operating mode. Default is <see cref="HttpRecordingMode.Auto"/>.</summary>
    public HttpRecordingMode Mode { get; set; } = HttpRecordingMode.Auto;

    /// <summary>Gets or sets the behavior when no recorded response matches during replay. Default is <see cref="HttpRecordingMissBehavior.Throw"/>.</summary>
    public HttpRecordingMissBehavior MissBehavior { get; set; } = HttpRecordingMissBehavior.Throw;

    /// <summary>Gets or sets the request matcher used for fingerprinting. When <c>null</c>, the <see cref="DefaultHttpRequestMatcher"/> is used.</summary>
    public IHttpRequestMatcher? RequestMatcher { get; set; }

    /// <summary>Gets or sets the sanitizer applied to entries before persistence. When <c>null</c>, no sanitization is applied.</summary>
    public IHttpRecordingSanitizer? Sanitizer { get; set; }
}
