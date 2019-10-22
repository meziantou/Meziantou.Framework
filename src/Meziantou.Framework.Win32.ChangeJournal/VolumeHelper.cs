using System;
using System.Globalization;
using System.IO;

namespace Meziantou.Framework.Win32
{
    internal static class VolumeHelper
    {
        internal static string GetValidVolumePath(DriveInfo driveInfo)
        {
            string name;
            if (!string.IsNullOrEmpty(driveInfo.Name))
            {
                name = driveInfo.Name;
            }
            else if (!string.IsNullOrEmpty(driveInfo.VolumeLabel))
            {
                name = driveInfo.VolumeLabel;
            }
            else
            {
                throw new ArgumentException("Cannot determine the name of the drive", nameof(driveInfo));
            }

            name = name
                .Replace(":", "")
                .Replace(Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture), "");

            return string.Format("\\\\.\\{0}:", name);
        }
    }
}
