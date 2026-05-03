#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
#else
namespace Meziantou.Framework.SnapshotTesting.Utils;
#endif

internal static class FileInfoExtensions
{
    public static void TrySetReadOnly(this FileInfo fileInfo, bool readOnly)
    {
        try
        {
            fileInfo.IsReadOnly = readOnly;
        }
        catch
        {
            // Ignore
        }
    }
}
