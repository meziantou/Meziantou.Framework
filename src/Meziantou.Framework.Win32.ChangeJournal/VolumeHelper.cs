namespace Meziantou.Framework.Win32;

internal static class VolumeHelper
{
    /// <summary>
    /// Converts a drive into the device path that <c>CreateFile</c> expects for a volume, such as <c>\\.\C:</c>.
    /// Only drive-letter volumes are supported, which is all a <see cref="DriveInfo"/> can designate.
    /// </summary>
    internal static string GetValidVolumePath(DriveInfo driveInfo)
    {
        var name = driveInfo.Name;
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Cannot determine the name of the drive", nameof(driveInfo));

        // DriveInfo.Name is a root path such as "C:\", so strip the separators and the colon before rebuilding it.
        name = name.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).TrimEnd(':');
        return $@"\\.\{name}:";
    }
}
