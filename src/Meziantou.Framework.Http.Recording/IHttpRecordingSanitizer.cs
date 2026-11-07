namespace Meziantou.Framework.Http.Recording;

/// <summary>Defines the contract for sanitizing recorded HTTP entries.</summary>
/// <remarks>
/// Sanitizers run on both sides of matching: on entries before they are persisted, and on the entry built from an
/// incoming request before its fingerprint is computed. Applying the same transformation to both sides is what allows
/// a value to be redacted without breaking replay. A sanitizer must therefore be deterministic.
/// </remarks>
public interface IHttpRecordingSanitizer
{
    /// <summary>Sanitizes the entry in-place to redact sensitive data.</summary>
    void Sanitize(HttpRecordingEntry entry);
}
