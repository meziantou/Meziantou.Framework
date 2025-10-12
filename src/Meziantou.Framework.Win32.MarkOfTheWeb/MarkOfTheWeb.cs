using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;

namespace Meziantou.Framework.Win32;

public static class MarkOfTheWeb
{
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
    /// Files outside of the Local Computer, Trusted, and Intranet Zones
    /// are considered "Untrusted". Avoid connecting to the target
    /// server unless the URL's Zone is trustworthy.
    /// </summary>
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
