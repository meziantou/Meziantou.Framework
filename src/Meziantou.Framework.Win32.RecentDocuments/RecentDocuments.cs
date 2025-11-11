using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Provides methods to manage Windows Recent Documents.
/// </summary>
public static class RecentDocuments
{
    /// <summary>
    /// Notifies the system that an item has been accessed, for the purposes of tracking those items used most recently and most frequently.
    /// </summary>
    /// <param name="path">The path to the document that has been accessed.</param>
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe void AddToRecentDocuments(string path)
    {
        fixed (char* p = path)
        {
            Windows.Win32.PInvoke.SHAddToRecentDocs((uint)Windows.Win32.UI.Shell.SHARD.SHARD_PATHW, p);
        }
    }

    /// <summary>
    /// Clears all usage data.
    /// </summary>
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe void ClearRecentDocuments()
    {
        Windows.Win32.PInvoke.SHAddToRecentDocs((uint)Windows.Win32.UI.Shell.SHARD.SHARD_PIDL, pv: null);
    }
}