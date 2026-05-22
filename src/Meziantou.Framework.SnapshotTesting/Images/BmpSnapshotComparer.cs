namespace Meziantou.Framework.SnapshotTesting;

internal sealed class BmpSnapshotComparer : ISnapshotComparer
{
    public static BmpSnapshotComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        try
        {
            return Image.Load(expected.Data).Equals(Image.Load(actual.Data));
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
