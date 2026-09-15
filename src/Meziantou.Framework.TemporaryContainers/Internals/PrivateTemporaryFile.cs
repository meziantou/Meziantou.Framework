namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Creates the temporary files and directories the library writes content into. That content can hold secrets (a file written into a container, a build context, environment variables), and the temporary directory is shared with the other users of the machine on Linux, so the files are only readable by the current user.</summary>
internal static class PrivateTemporaryFile
{
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Builds the path of a temporary file that does not exist yet.</summary>
    public static string CreatePath() => Path.Combine(Path.GetTempPath(), "MezTC_" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates a new file only the current user can read. The creation fails when the path already exists, so a file planted at that path by somebody else is never written to.</summary>
    public static FileStream Create(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = System.IO.FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = FileMode;

        return new FileStream(path, options);
    }

    /// <summary>Creates a new directory only the current user can read.</summary>
    public static string CreateDirectory()
    {
        var path = CreatePath();
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, DirectoryMode);

        return path;
    }

    /// <summary>Deletes a temporary directory without ever throwing: it only ever holds a copy of content the caller already has.</summary>
    public static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
