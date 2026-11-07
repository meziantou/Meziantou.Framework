namespace Meziantou.Framework;

/// <summary>Creates the directories and files backing <see cref="TemporaryDirectory"/> and <see cref="TemporaryFile"/> so that other users cannot read them.</summary>
internal static class TemporaryPaths
{
    private const UnixFileMode OwnerOnlyDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates the shared root directory, or validates it when it already exists.</summary>
    /// <exception cref="UnauthorizedAccessException">The root directory already exists and is accessible to other users.</exception>
    public static void EnsureRootDirectory(FullPath rootDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            // The Windows temporary folder is already per-user.
            Directory.CreateDirectory(rootDirectory);
            return;
        }

        if (Directory.Exists(rootDirectory))
        {
            var mode = File.GetUnixFileMode(rootDirectory.Value);
            if ((mode & ~OwnerOnlyDirectoryMode) == default)
                return;

            // The root has a well-known name, so it may predate this check, or another user may have created it
            // first on a shared temporary folder. Only the owner of a directory can change its mode, so tightening
            // it both repairs a root owned by this user and rejects one owned by somebody else.
            try
            {
                File.SetUnixFileMode(rootDirectory.Value, OwnerOnlyDirectoryMode);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"The temporary root directory '{rootDirectory}' is owned by another user and is accessible to them (mode: {mode}). Use an explicit root directory.", ex);
            }

            return;
        }

        Directory.CreateDirectory(rootDirectory.Value, OwnerOnlyDirectoryMode);
    }

    /// <summary>Creates a directory that only the current user can access.</summary>
    public static void CreateDirectory(FullPath path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path.Value, OwnerOnlyDirectoryMode);
    }

    /// <summary>Creates a new file that only the current user can access, along with its parent directories.</summary>
    /// <exception cref="IOException">The file already exists.</exception>
    public static void CreateFile(FullPath path)
    {
        var parent = path.Parent;
        if (!parent.IsEmpty)
        {
            CreateDirectory(parent);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFileMode;
        }

        using var stream = new FileStream(path, options);
    }
}
