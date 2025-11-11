using System.Collections.Immutable;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a disk drive including name, format, type, free space, and total size.</summary>
public sealed class DriveSnapshot
{
    private DriveSnapshot(DriveInfo driveInfo)
    {
        Name = Utils.SafeGet(() => driveInfo.Name);
        DriveFormat = Utils.SafeGet(() => driveInfo.DriveFormat);
        DriveType = Utils.SafeGet(() => driveInfo.DriveType);
        VolumeLabel = Utils.SafeGet(() => driveInfo.VolumeLabel);
        AvailableFreeSpace = Utils.SafeGet(() => driveInfo.AvailableFreeSpace);
        TotalFreeSpace = Utils.SafeGet(() => driveInfo.TotalFreeSpace);
        TotalSize = Utils.SafeGet(() => driveInfo.TotalSize);
        IsReady = Utils.SafeGet(() => driveInfo.IsReady);
        RootDirectory = Utils.SafeGet(() => driveInfo.RootDirectory.FullName);
    }

    public string Name { get; }
    public string DriveFormat { get; }
    public DriveType DriveType { get; }
    public string VolumeLabel { get; }
    public long AvailableFreeSpace { get; }
    public long TotalFreeSpace { get; }
    public long TotalSize { get; }
    public bool IsReady { get; }
    public string RootDirectory { get; }

    internal static ImmutableArray<DriveSnapshot> Get()
    {
        return DriveInfo.GetDrives()
            .Select(drive => new DriveSnapshot(drive))
            .ToImmutableArray();
    }
}
