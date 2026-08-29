namespace Meziantou.Framework.Win32;

/// <summary>Represents the result of an AMSI scan.</summary>
/// <remarks>
/// An antimalware provider may return any value between <see cref="NotDetected"/> and 32767 as an estimated risk
/// level: the larger the value, the riskier it is to continue with the content. Those values are provider specific
/// and may identify a malware family or ID.
/// </remarks>
// https://learn.microsoft.com/en-us/windows/win32/api/amsi/ne-amsi-amsi_result
public enum AmsiResult
{
    /// <summary>Known good. No detection found, and the result is likely not going to change after a future definition update.</summary>
    Clean = 0,

    /// <summary>No detection found, but the result might change after a future definition update.</summary>
    NotDetected = 1,

    /// <summary>Administrator policy blocked this content on this machine (beginning of range).</summary>
    BlockedByAdminStart = 16384,

    /// <summary>Administrator policy blocked this content on this machine (end of range).</summary>
    BlockedByAdminEnd = 20479,

    /// <summary>Detection found. The content is considered malware and should be blocked.</summary>
    Detected = 32768,
}
