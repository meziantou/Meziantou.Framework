namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Represents a notification subscription for file system events.</summary>
public sealed class Notification
{
    /// <summary>Initializes a new instance of the <see cref="Notification"/> class for all paths.</summary>
    /// <param name="notificationType">The types of notifications to receive.</param>
    public Notification(PRJ_NOTIFY_TYPES notificationType)
        : this(path: null, notificationType)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Notification"/> class for a specific path.</summary>
    /// <param name="path">The path to receive notifications for, or <see langword="null"/> for all paths.</param>
    /// <param name="notificationType">The types of notifications to receive.</param>
    public Notification(string? path, PRJ_NOTIFY_TYPES notificationType)
    {
        Path = path;
        NotificationType = notificationType;
    }

    /// <summary>Gets the path to receive notifications for, or <see langword="null"/> for all paths.</summary>
    public string? Path { get; }

    /// <summary>Gets the types of notifications to receive.</summary>
    public PRJ_NOTIFY_TYPES NotificationType { get; }
}
