namespace Meziantou.Framework.Http.Recording;

/// <summary>Defines the contract for computing a deterministic fingerprint from an HTTP request.</summary>
public interface IHttpRequestMatcher
{
    /// <summary>Computes a deterministic fingerprint string for a request. Two requests that should match must produce the same fingerprint.</summary>
    string ComputeFingerprint(HttpRecordingEntry entry);
}
