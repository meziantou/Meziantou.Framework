namespace Meziantou.Framework.Http.Recording;

/// <summary>Specifies the operating mode for the recording handler.</summary>
public enum HttpRecordingMode
{
    /// <summary>Execute real HTTP calls, record request/response pairs, and persist to storage.</summary>
    Record,

    /// <summary>Intercept HTTP calls and return recorded responses. No external HTTP calls are made.</summary>
    Replay,

    /// <summary>Replay if a match exists; otherwise, execute real call and record it.</summary>
    Auto,
}
