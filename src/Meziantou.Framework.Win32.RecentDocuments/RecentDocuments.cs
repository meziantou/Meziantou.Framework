using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Meziantou.Framework.Win32;

/// <summary>Provides methods to manage Windows Recent Documents.</summary>
public static class RecentDocuments
{
    private const int MaxApplicationUserModelIdLength = 128;

    /// <summary>Notifies the system that an item has been accessed, for the purposes of tracking those items used most recently and most frequently.</summary>
    /// <param name="path">The path to the document that has been accessed. A relative path is resolved against the current directory.</param>
    /// <remarks>
    /// <para>The item is added to the user's Recent Items and to the Jump List of the application associated with the current process.</para>
    /// <para>Executable (.exe) files are accepted but never appear in Recent Items. Folders only appear in the File Explorer Jump List.</para>
    /// <para>If the application is not registered to handle the file type, Windows may register it for that file type for the current user.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or contains a null character.</exception>
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe void AddToRecentDocuments(string path)
    {
        // A null pointer makes SHAddToRecentDocs clear all usage data on all items, so an unvalidated
        // null path would silently erase the recent documents instead of adding one.
        ArgumentException.ThrowIfNullOrEmpty(path);

        // The shell does not know the current directory, and would stop reading at an embedded null character.
        // GetFullPath resolves the former and rejects the latter.
        path = Path.GetFullPath(path);

        fixed (char* p = path)
        {
            PInvoke.SHAddToRecentDocs((uint)SHARD.SHARD_PATHW, p);
        }
    }

    /// <summary>Notifies the system that an item has been accessed by the application identified by <paramref name="applicationUserModelId"/>, for the purposes of tracking those items used most recently and most frequently.</summary>
    /// <param name="path">The path to the document that has been accessed. A relative path is resolved against the current directory.</param>
    /// <param name="applicationUserModelId">The Application User Model ID (AppUserModelID) of the application whose Jump List receives the item.</param>
    /// <remarks>
    /// <para>Use this overload when the Jump List belongs to a different identity than the current process, for instance when the application sets an explicit AppUserModelID or is started through <c>dotnet app.dll</c>.</para>
    /// <para>Executable (.exe) files are accepted but never appear in Recent Items. Folders only appear in the File Explorer Jump List.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="applicationUserModelId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty, contains a null character, or does not identify an existing item;
    /// or <paramref name="applicationUserModelId"/> is empty, longer than 128 characters, or contains a space or a null character.
    /// </exception>
    [SupportedOSPlatform("windows6.1")]
    public static unsafe void AddToRecentDocuments(string path, string applicationUserModelId)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(applicationUserModelId);
        if (applicationUserModelId.Length > MaxApplicationUserModelIdLength)
            throw new ArgumentException($"The AppUserModelID cannot be longer than {MaxApplicationUserModelIdLength} characters.", nameof(applicationUserModelId));

        if (applicationUserModelId.AsSpan().ContainsAny(' ', '\0'))
            throw new ArgumentException("The AppUserModelID cannot contain a space or a null character.", nameof(applicationUserModelId));

        path = Path.GetFullPath(path);

        var pidl = PInvoke.ILCreateFromPath(path);
        if (pidl is null)
            throw new ArgumentException($"The path '{path}' does not identify an existing item.", nameof(path));

        try
        {
            fixed (char* appId = applicationUserModelId)
            {
                var info = new SHARDAPPIDINFOIDLIST
                {
                    pidl = pidl,
                    pszAppID = appId,
                };

                PInvoke.SHAddToRecentDocs((uint)SHARD.SHARD_APPIDINFOIDLIST, &info);
            }
        }
        finally
        {
            PInvoke.ILFree(pidl);
        }
    }

    /// <summary>Clears all usage data on all items.</summary>
    /// <remarks>
    /// This is not limited to the calling application: it empties the user's Recent Items and the Recent and Frequent lists of every application's Jump List. It cannot be undone.
    /// </remarks>
    [SupportedOSPlatform("windows5.1.2600")]
    public static unsafe void ClearRecentDocuments()
    {
        PInvoke.SHAddToRecentDocs((uint)SHARD.SHARD_PIDL, pv: null);
    }
}
