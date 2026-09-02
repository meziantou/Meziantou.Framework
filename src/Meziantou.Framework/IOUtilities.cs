namespace Meziantou.Framework;

#if PUBLIC_IO_UTILITIES
public
#else
internal
#endif
static partial class IOUtilities
{
    private static readonly string[] ReservedFileNames =
    [
        "con", "prn", "aux", "nul",
        "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    ];

    /// <summary>Makes sure a directory exists for a given file path.</summary>
    /// <param name="filePath">The file path. Note this is not to be confused with the directory path. May not be null.</param>
    public static void PathCreateDirectory(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!Path.IsPathRooted(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }

        var dir = Path.GetDirectoryName(filePath);
        if (dir is null)
            return;

        Directory.CreateDirectory(dir);
    }

    /// <summary>Unprotects the given file path.</summary>
    /// <param name="path">The file path. May not be null.</param>
    public static void PathUnprotect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fi = new FileInfo(path);
        if (fi.Exists)
        {
            if (fi.IsReadOnly)
            {
                fi.IsReadOnly = false;
            }
        }
    }

    /// <summary>Converts a text into a valid file name.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="reservedNameFormat">The reserved format to use for reserved names. If null '_{0}_' will be used.</param>
    /// <param name="reservedCharFormat">The reserved format to use for reserved characters. If null '_x{0}_' will be used.</param>
    /// <returns>A valid file name.</returns>
    public static string ToValidFileName(string fileName, string reservedNameFormat = "_{0}_", string reservedCharFormat = "_x{0}_")
    {
        ArgumentNullException.ThrowIfNull(fileName);

        ArgumentNullException.ThrowIfNull(reservedNameFormat);

        ArgumentNullException.ThrowIfNull(reservedCharFormat);

        // Windows reserves a device name with any extension, so "con.txt" is as reserved as "con".
        // The device name is the part before the first period.
        var dotIndex = fileName.IndexOf('.', StringComparison.Ordinal);
        var deviceName = dotIndex < 0 ? fileName : fileName[..dotIndex];
        if (ReservedFileNames.ContainsIgnoreCase(deviceName) || IsAllDots(fileName))
        {
            return string.Format(CultureInfo.InvariantCulture, reservedNameFormat, fileName);
        }

        var invalid = Path.GetInvalidFileNameChars();

        // Windows silently strips trailing periods and spaces, so a name keeping them would be
        // created under a different name than the caller was given. They are escaped like any other
        // character that cannot be used as-is.
        var trailingStart = fileName.Length;
        while (trailingStart > 0 && fileName[trailingStart - 1] is '.' or ' ')
        {
            trailingStart--;
        }

        var sb = new StringBuilder(fileName.Length);
        for (var i = 0; i < fileName.Length; i++)
        {
            var c = fileName[i];
            if (Array.IndexOf(invalid, c) >= 0 || i >= trailingStart)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, reservedCharFormat, (short)c);
            }
            else
            {
                sb.Append(c);
            }
        }

        var s = sb.ToString();
        if (string.Equals(s, fileName, StringComparison.Ordinal))
        {
            s = fileName;
        }

        return s;
    }

    private static bool IsAllDots(string fileName)
    {
        foreach (var c in fileName)
        {
            if (c != '.')
                return false;
        }

        return true;
    }

    public static void CopyDirectory(string sourcePath, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        ArgumentNullException.ThrowIfNull(destinationPath);

        // Get the subdirectories for the specified directory.
        var dir = new DirectoryInfo(sourcePath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourcePath);

        var dirs = dir.GetDirectories();
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
        }

        // Get the files in the directory and copy them to the new location.
        var files = dir.GetFiles();
        foreach (var file in files)
        {
            var temppath = Path.Combine(destinationPath, file.Name);
            file.CopyTo(temppath, overwrite: false);
        }

        // Copy subdirectories
        foreach (var subdir in dirs)
        {
            var tempPath = Path.Combine(destinationPath, subdir.Name);
            CopyDirectory(subdir.FullName, tempPath);
        }
    }

    public static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
    {
        if (!source.Exists)
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);

        destination.Create();

        // Get the files in the directory and copy them to the new location.
        var files = source.GetFiles();
        foreach (var file in files)
        {
            var temppath = Path.Combine(destination.FullName, file.Name);
            file.CopyTo(temppath, overwrite: false);
        }

        // Copy subdirectories
        var dirs = source.GetDirectories();
        foreach (var subdir in dirs)
        {
            var temppath = new DirectoryInfo(Path.Combine(destination.FullName, subdir.Name));
            CopyDirectory(subdir, temppath);
        }
    }
}
