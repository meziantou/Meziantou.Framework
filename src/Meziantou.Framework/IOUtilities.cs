using System.Buffers;

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

    private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create(Path.GetInvalidFileNameChars());

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

        // Windows silently strips trailing periods and spaces, so a name keeping them would be
        // created under a different name than the caller was given. They are escaped like any other
        // character that cannot be used as-is.
        var trailingStart = fileName.Length;
        while (trailingStart > 0 && fileName[trailingStart - 1] is '.' or ' ')
        {
            trailingStart--;
        }

        var firstInvalid = fileName.AsSpan(0, trailingStart).IndexOfAny(InvalidFileNameChars);
        if (firstInvalid < 0)
        {
            if (trailingStart == fileName.Length)
                return fileName;

            firstInvalid = trailingStart;
        }

        var sb = new StringBuilder(fileName.Length + 8);
        sb.Append(fileName, 0, firstInvalid);
        for (var i = firstInvalid; i < fileName.Length; i++)
        {
            var c = fileName[i];
            if (i >= trailingStart || InvalidFileNameChars.Contains(c))
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, reservedCharFormat, (short)c);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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

    /// <summary>Copies a directory and its content to a new location.</summary>
    /// <remarks>Symbolic links to directories are copied as links instead of being followed, as <c>cp -R</c> does.</remarks>
    public static void CopyDirectory(string sourcePath, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        ArgumentNullException.ThrowIfNull(destinationPath);

        CopyDirectory(new DirectoryInfo(sourcePath), new DirectoryInfo(destinationPath));
    }

    /// <summary>Copies a directory and its content to a new location.</summary>
    /// <remarks>Symbolic links to directories are copied as links instead of being followed, as <c>cp -R</c> does.</remarks>
    public static void CopyDirectory(DirectoryInfo source, DirectoryInfo destination)
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(destination);

        if (!source.Exists)
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);

        CopyDirectoryContent(source, destination);
    }

    private static void CopyDirectoryContent(DirectoryInfo source, DirectoryInfo destination)
    {
        destination.Create();

        foreach (var file in source.GetFiles())
        {
            var destinationPath = Path.Combine(destination.FullName, file.Name);
            file.CopyTo(destinationPath, overwrite: false);
        }

        foreach (var subdirectory in source.GetDirectories())
        {
            var destinationPath = Path.Combine(destination.FullName, subdirectory.Name);

            // Following a link would copy its target, and a link to an ancestor would recurse until the path is too long
            if (subdirectory.LinkTarget is { } linkTarget)
            {
                Directory.CreateSymbolicLink(destinationPath, linkTarget);
                continue;
            }

            CopyDirectoryContent(subdirectory, new DirectoryInfo(destinationPath));
        }
    }
}
