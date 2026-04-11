namespace Meziantou.Framework.Http.Recording;

/// <summary>Defines the contract for sanitizing recorded HTTP entries before persistence.</summary>
public interface IHttpRecordingSanitizer
{
    /// <summary>Sanitizes the entry in-place to redact sensitive data before it is persisted.</summary>
    void Sanitize(HttpRecordingEntry entry);
}
