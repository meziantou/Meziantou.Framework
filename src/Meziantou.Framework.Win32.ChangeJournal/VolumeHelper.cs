using System.Collections.Generic;
using System.IO;

namespace Meziantou.Framework.Win32
{
    public static class VolumeHelper
    {
        public static IEnumerable<string> GetValidVolumes()
        {
            foreach (var info in DriveInfo.GetDrives())
            {
                if (!info.IsReady || info.DriveFormat != "NTFS")
                    continue;

                if (!string.IsNullOrEmpty(info.Name))
                    yield return info.Name;
                else if (!string.IsNullOrEmpty(info.VolumeLabel))
                    yield return info.VolumeLabel;
            }
        }

        public static IEnumerable<string> GetValidVolumePaths()
        {
            foreach (var volume in GetValidVolumes())
                yield return GetValidVolumePath(volume);
        }

        public static string GetValidVolumePath(string driveNameOrLabel)
        {
            var temp = driveNameOrLabel
                .Replace(":", "")
                .Replace(Path.DirectorySeparatorChar.ToString(), "");

            return string.Format("{0}{0}.{0}{1}:", Path.DirectorySeparatorChar, temp);
        }

        public static bool VolumePathIsValid(string formattedPath)
        {
            return formattedPath.StartsWith("\\\\.\\") && formattedPath.EndsWith(":");
        }
    }
}