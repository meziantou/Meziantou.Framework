#nullable disable
namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    public class Notification
    {
        public Notification(PRJ_NOTIFY_TYPES notificationType)
            : this(path: null, notificationType)
        {
        }

        public Notification(string path, PRJ_NOTIFY_TYPES notificationType)
        {
            Path = path;
            NotificationType = notificationType;
        }

        public string Path { get; }
        public PRJ_NOTIFY_TYPES NotificationType { get; }
    }
}
