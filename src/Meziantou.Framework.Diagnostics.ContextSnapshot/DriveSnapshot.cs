using System.Collections.Immutable;
using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a drive at a specific point in time.</summary>
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

    /// <summary>Gets the name of the drive.</summary>
    public string Name { get; }
    /// <summary>Gets the file system format of the drive.</summary>
    public string DriveFormat { get; }
    /// <summary>Gets the type of the drive.</summary>
    public DriveType DriveType { get; }
    /// <summary>Gets the volume label of the drive.</summary>
    public string VolumeLabel { get; }
    /// <summary>Gets the amount of available free space on the drive in bytes.</summary>
    public long AvailableFreeSpace { get; }
    /// <summary>Gets the total amount of free space on the drive in bytes.</summary>
    public long TotalFreeSpace { get; }
    /// <summary>Gets the total size of the drive in bytes.</summary>
    public long TotalSize { get; }
    /// <summary>Gets a value indicating whether the drive is ready.</summary>
    public bool IsReady { get; }
    /// <summary>Gets the root directory of the drive.</summary>
    public string RootDirectory { get; }

    internal static ImmutableArray<DriveSnapshot> Get()
    {
        return DriveInfo.GetDrives()
            .Select(drive => new DriveSnapshot(drive))
            .ToImmutableArray();
    }
}
