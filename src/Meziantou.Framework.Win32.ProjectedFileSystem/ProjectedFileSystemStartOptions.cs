namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Configuration options for starting a virtual file system.</summary>
public sealed class ProjectedFileSystemStartOptions
{
    /// <summary>Gets or sets a value indicating whether to cache queries for non-existent paths to improve performance.</summary>
    public bool UseNegativePathCache { get; set; }

    /// <summary>Gets the list of notification subscriptions for file system events.</summary>
    // https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/ns-projectedfslib-prj_notification_mapping
    public IList<Notification> Notifications { get; } = new List<Notification>();
}
