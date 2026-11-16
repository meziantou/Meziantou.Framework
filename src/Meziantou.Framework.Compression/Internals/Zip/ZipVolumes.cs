using System.Globalization;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>Finds the volumes of a split zip archive next to one of them.</summary>
internal static class ZipVolumes
{
    /// <summary>
    /// Returns the paths of the volumes, in order: the Info-ZIP <c>.z01</c>, <c>.z02</c>, ... volumes followed by the
    /// <c>.zip</c> one, or the <c>.001</c>, <c>.002</c>, ... parts of 7-Zip, or <paramref name="path"/> alone.
    /// </summary>
    public static List<string> Find(string path)
    {
        var extension = Path.GetExtension(path);

        // 7-Zip parts: archive.zip.001, archive.zip.002, ...
        if (string.Equals(extension, ".001", StringComparison.Ordinal))
        {
            var basePath = path[..^extension.Length];
            var parts = new List<string> { path };
            for (var i = 2; i <= 999; i++)
            {
                var part = basePath + "." + i.ToString("000", CultureInfo.InvariantCulture);
                if (!File.Exists(part))
                    break;

                parts.Add(part);
            }

            return parts;
        }

        // Info-ZIP volumes: archive.z01, archive.z02, ..., archive.zip (the last volume, which has the central directory)
        string zipPath;
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            zipPath = path;
        }
        else if (extension.Length is 4 && extension[1] is 'z' or 'Z' && char.IsAsciiDigit(extension[2]) && char.IsAsciiDigit(extension[3]))
        {
            zipPath = Path.ChangeExtension(path, extension[1] is 'Z' ? ".ZIP" : ".zip");
            if (!File.Exists(zipPath))
                return [path];
        }
        else
        {
            return [path];
        }

        var volumes = new List<string>();
        var prefix = zipPath[..^4] + (zipPath[^3] is 'Z' ? ".Z" : ".z");
        for (var i = 1; i <= 99; i++)
        {
            var volume = prefix + i.ToString("00", CultureInfo.InvariantCulture);
            if (!File.Exists(volume))
                break;

            volumes.Add(volume);
        }

        if (volumes.Count is 0)
            return [path];

        volumes.Add(zipPath);
        return volumes;
    }
}
