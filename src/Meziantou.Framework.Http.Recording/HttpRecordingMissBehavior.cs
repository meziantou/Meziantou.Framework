namespace Meziantou.Framework.Http.Recording;

/// <summary>Specifies what happens when no recorded response matches an incoming request during replay.</summary>
public enum HttpRecordingMissBehavior
{
    /// <summary>Throw an <see cref="HttpRecordingMissException"/>.</summary>
    Throw,

    /// <summary>Return a default HTTP 500 response with a diagnostic body.</summary>
    ReturnDefault,

    /// <summary>Forward the request to the inner handler (make a real HTTP call).</summary>
    Passthrough,
}
