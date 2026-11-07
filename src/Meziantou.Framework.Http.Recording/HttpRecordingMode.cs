namespace Meziantou.Framework.Http.Recording;

/// <summary>Specifies the operating mode for the recording handler.</summary>
public enum HttpRecordingMode
{
    /// <summary>
    /// Execute real HTTP calls, record request/response pairs, and persist to storage.
    /// Existing recordings are not loaded, so saving replaces the previous content of the store rather than appending to it.
    /// </summary>
    Record,

    /// <summary>Intercept HTTP calls and return recorded responses. No external HTTP calls are made unless <see cref="HttpRecordingMissBehavior.Passthrough"/> is configured.</summary>
    Replay,

    /// <summary>
    /// Replay if a match exists among the previously stored recordings; otherwise apply <see cref="HttpRecordingOptions.MissBehavior"/>.
    /// Entries recorded during the current session are persisted but are not replayed within that same session.
    /// </summary>
    Auto,
}
