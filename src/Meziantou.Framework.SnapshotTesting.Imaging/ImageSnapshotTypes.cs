namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>The snapshot types of the image formats that <see cref="SnapshotType"/> does not name.</summary>
internal static class ImageSnapshotTypes
{
    public static SnapshotType Qoi { get; } = SnapshotType.Create("qoi");
    public static SnapshotType Tga { get; } = SnapshotType.Create("tga");
    public static SnapshotType Pnm { get; } = SnapshotType.Create("pnm");
    public static SnapshotType Cur { get; } = SnapshotType.Create("cur");
    public static SnapshotType Ani { get; } = SnapshotType.Create("ani");
}
