using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Provides methods to interact with the Mark of the Web (MOTW) security feature in Windows.
/// MOTW helps protect users from potentially unsafe content downloaded from the internet by storing
/// the origin zone information in an Alternate Data Stream (ADS) named "Zone.Identifier".
/// </summary>
/// <example>
/// Add the Mark of the Web to a file:
/// <code>
/// MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);
/// </code>
/// Get the zone of a file:
/// <code>
/// var zone = MarkOfTheWeb.GetFileZone(path);
/// </code>
/// Remove the Mark of the Web from a file:
/// <code>
/// MarkOfTheWeb.RemoveFileZone(path);
/// </code>
/// </example>
public static class MarkOfTheWeb
{
    /// <summary>Removes the Mark of the Web zone information from a file by deleting the Zone.Identifier alternate data stream.</summary>
    /// <param name="filePath">The path to the file from which to remove the zone information.</param>
    public static void RemoveFileZone(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        filePath = Path.GetFullPath(filePath);
        var adsPath = filePath + ":Zone.Identifier";
        try
        {
            File.Delete(adsPath);
        }
        catch (FileNotFoundException)
        {
        }
    }

    /// <summary>Gets the security zone of a file using the Windows Security Manager COM API.</summary>
    /// <param name="filePath">The path to the file to query.</param>
    /// <returns>The <see cref="UrlZone"/> of the file, or <see cref="UrlZone.Invalid"/> if the zone cannot be determined.</returns>
    [SupportedOSPlatform("windows")]
    public static UrlZone GetFileZone(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            return UrlZone.Invalid;

        filePath = Path.GetFullPath(filePath);

        try
        {
            var hr = PInvoke.CoInternetCreateSecurityManager(pSP: null, out var securityManager, dwReserved: 0);
            if (hr.Failed || securityManager is null)
                return UrlZone.Invalid;

            try
            {
                securityManager.MapUrlToZone(filePath, out var zone, dwFlags: 0);
                return (UrlZone)zone;
            }
            finally
            {
                Marshal.ReleaseComObject(securityManager);
            }
        }
        catch
        {
            // Ignore errors
        }

        return UrlZone.Invalid;
    }

    /// <summary>Gets the raw content of the Zone.Identifier alternate data stream from a file.</summary>
    /// <param name="filePath">The path to the file to read.</param>
    /// <returns>The content of the Zone.Identifier stream, or <see langword="null"/> if the file does not have zone information.</returns>
    public static string GetFileZoneContent(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        filePath = Path.GetFullPath(filePath);
        var adsPath = filePath + ":Zone.Identifier";

        try
        {
            return File.ReadAllText(adsPath, Encoding.Unicode);
        }
        catch (FileNotFoundException)
        {
        }

        return null;
    }

    /// <summary>Sets the Mark of the Web zone information for a file by writing to the Zone.Identifier alternate data stream.</summary>
    /// <param name="filePath">The path to the file to mark.</param>
    /// <param name="zone">The security zone to assign to the file.</param>
    /// <param name="referrerUrl">Optional URL of the page that linked to the file.</param>
    /// <param name="hostUrl">Optional URL of the host from which the file was downloaded.</param>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public static void SetFileZone(string filePath, UrlZone zone, string? referrerUrl = null, string? hostUrl = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        filePath = Path.GetFullPath(filePath);
        var adsPath = filePath + ":Zone.Identifier";
        using var writer = new StreamWriter(adsPath, append: false, Encoding.Unicode);
        writer.WriteLine("[ZoneTransfer]");
        writer.WriteLine("ZoneId=" + ((int)zone).ToString(CultureInfo.InvariantCulture));
        if (referrerUrl is not null)
        {
            writer.WriteLine("ReferrerUrl=" + referrerUrl);
        }

        if (hostUrl is not null)
        {
            writer.WriteLine("HostUrl=" + hostUrl);
        }
    }

    /// <summary>
    /// Determines whether a file is from an untrusted source based on its security zone.
    /// Files outside of the Local Computer, Trusted, and Intranet zones are considered untrusted.
    /// </summary>
    /// <param name="filePath">The path to the file to check.</param>
    /// <returns><see langword="true"/> if the file is from an untrusted zone (Internet or Restricted); otherwise, <see langword="false"/>.</returns>
    [SupportedOSPlatform("windows")]
    public static bool IsUntrusted(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            return false;

        filePath = Path.GetFullPath(filePath);

        var hr = PInvoke.CoInternetCreateSecurityManager(pSP: null, out var securityManager, dwReserved: 0);
        if (hr.Failed || securityManager is null)
            return true;

        try
        {
            securityManager.MapUrlToZone(filePath, out var zone, dwFlags: PInvoke.MUTZ_NOSAVEDFILECHECK);
            if (zone >= (int)UrlZone.Internet)
                return true;

            // For files currently stored in trusted locations, ensure we also look for any MotW storing the original source location
            securityManager.MapUrlToZone(filePath, out zone, dwFlags: PInvoke.MUTZ_REQUIRESAVEDFILECHECK);
            if (zone >= (int)UrlZone.Internet)
                return true;

            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(securityManager);
        }
    }
}
