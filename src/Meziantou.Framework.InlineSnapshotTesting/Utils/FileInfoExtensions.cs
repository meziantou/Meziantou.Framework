namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
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
