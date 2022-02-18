namespace Meziantou.Framework.Win32.ProjectedFileSystem;

public sealed class ProjectedFileSystemStartOptions
{
    public bool UseNegativePathCache { get; set; }

    // https://docs.microsoft.com/en-us/windows/desktop/api/projectedfslib/ns-projectedfslib-prj_notification_mapping
    public IList<Notification> Notifications { get; } = new List<Notification>();
}
