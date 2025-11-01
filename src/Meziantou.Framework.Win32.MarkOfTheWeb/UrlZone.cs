namespace Meziantou.Framework.Win32;

/// <summary>Represents the security zone of a URL or file in Windows Internet Explorer security settings.</summary>
public enum UrlZone
{
    /// <summary>Invalid or unknown zone.</summary>
    Invalid = -1,

    /// <summary>Local Machine zone - files on the local computer.</summary>
    LocalMachine = 0,

    /// <summary>Local Intranet zone - addresses on a local network.</summary>
    Intranet = 1,

    /// <summary>Trusted Sites zone - sites that have been explicitly trusted.</summary>
    Trusted = 2,

    /// <summary>Internet zone - sites on the Internet that are not in other zones.</summary>
    Internet = 3,

    /// <summary>Restricted Sites zone - sites that are explicitly untrusted and potentially unsafe.</summary>
    Untrusted = 4,
}