namespace Meziantou.Framework.Http.Recording;

/// <summary>Configuration options for the HTTP recording handler.</summary>
public sealed class HttpRecordingOptions
{
    /// <summary>Gets or sets the operating mode. Default is <see cref="HttpRecordingMode.Auto"/>.</summary>
    public HttpRecordingMode Mode { get; set; } = HttpRecordingMode.Auto;

    /// <summary>Gets or sets the behavior when no recorded response matches an incoming request.</summary>
    /// <remarks>
    /// When <see langword="null"/> (the default), the behavior depends on <see cref="Mode"/>:
    /// <see cref="HttpRecordingMissBehavior.Throw"/> for <see cref="HttpRecordingMode.Replay"/>, and
    /// <see cref="HttpRecordingMissBehavior.Passthrough"/> for <see cref="HttpRecordingMode.Auto"/> so that a missing
    /// recording is created. Setting it explicitly overrides that default in both modes: in particular,
    /// <see cref="HttpRecordingMode.Auto"/> combined with <see cref="HttpRecordingMissBehavior.Throw"/> never performs
    /// a real HTTP call. This option is not used in <see cref="HttpRecordingMode.Record"/>.
    /// </remarks>
    public HttpRecordingMissBehavior? MissBehavior { get; set; }

    /// <summary>Gets or sets the request matcher used for fingerprinting. When <see langword="null"/>, the <see cref="DefaultHttpRequestMatcher"/> is used.</summary>
    public IHttpRequestMatcher? RequestMatcher { get; set; }

    /// <summary>Gets the sanitizers applied to entries before persistence, and to incoming requests before matching. Empty by default, meaning no sanitization.</summary>
    public IList<IHttpRecordingSanitizer> Sanitizers { get; } = [];

    /// <summary>Gets or sets a value indicating whether recordings are saved when the handler is disposed asynchronously. Default is <see langword="false"/>.</summary>
    /// <remarks>Auto-saving requires disposing the handler with <see cref="IAsyncDisposable.DisposeAsync"/>; a synchronous <see cref="IDisposable.Dispose"/> cannot save.</remarks>
    public bool AutoSave { get; set; }
}
