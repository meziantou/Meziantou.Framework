using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>Represents a temporary file that is automatically deleted when disposed.</summary>
/// <example>
/// <code>
/// // Basic usage with using statement
/// using var tempFile = TemporaryFile.Create();
/// File.WriteAllText(tempFile, "content");
///
/// // Provide a file name
/// using var tempFile2 = TemporaryFile.Create("custom.txt");
///
/// // Provide a full path
/// using var tempFile3 = TemporaryFile.Create(@"C:\temp\custom.txt");
/// </code>
/// </example>
/// <remarks>
/// This class creates a unique temporary file that is automatically cleaned up when disposed.
/// When providing a file name, the file is created under the system temp path in a "MezTF" subdirectory.
/// </remarks>
[DebuggerDisplay("{FullPath}")]
public sealed class TemporaryFile : IDisposable, IAsyncDisposable
{
    // Set when this instance created a directory for the sole purpose of holding the file.
    // Disposing must then remove that directory, not just the file.
    private readonly FullPath _ownedDirectory;
    private bool _disposed;

    /// <summary>Gets the full path to the temporary file.</summary>
    /// <value>The absolute path to the temporary file.</value>
    public FullPath FullPath { get; }

    private TemporaryFile(FullPath fullPath, FullPath ownedDirectory)
    {
        FullPath = fullPath;
        _ownedDirectory = ownedDirectory;
    }

    /// <summary>Creates a new temporary file in the system's default temp location.</summary>
    /// <returns>A new <see cref="TemporaryFile"/> instance.</returns>
    /// <remarks>
    /// The file is created under the system temp path in a "MezTF" subdirectory.
    /// The file name includes a timestamp and GUID to ensure uniqueness.
    /// </remarks>
    public static TemporaryFile Create()
    {
        var rootDirectory = FullPath.Combine(Path.GetTempPath(), "MezTF");
        var fileName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N") + ".tmp";
        TemporaryPaths.EnsureRootDirectory(rootDirectory);
        return CreateInRoot(rootDirectory, fileName, ownedDirectory: default);
    }

    /// <summary>Creates a new temporary file using the specified file name or full path.</summary>
    /// <param name="fileNameOrPath">The file name or full path for the temporary file.</param>
    /// <returns>A new <see cref="TemporaryFile"/> instance.</returns>
    /// <remarks>
    /// If <paramref name="fileNameOrPath"/> is not rooted, it is created under the system temp path in a "MezTF" subdirectory.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="fileNameOrPath"/> is <see langword="null"/>, empty, or contains only white-space characters.</exception>
    /// <exception cref="IOException">The file already exists.</exception>
    public static TemporaryFile Create(string fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
            throw new ArgumentException("The value cannot be null or whitespace.", nameof(fileNameOrPath));

        if (Path.IsPathRooted(fileNameOrPath))
            return Create(FullPath.FromPath(fileNameOrPath));

        TemporaryPaths.EnsureRootDirectory(FullPath.GetTempPath() / "MezTF");
        var rootDirectory = FullPath.GetTempPath() / "MezTF" / CreateUniqueFolderName();
        return CreateInRoot(rootDirectory, fileNameOrPath, ownedDirectory: rootDirectory);
    }

    /// <summary>Creates a new temporary file at the specified path.</summary>
    /// <param name="filePath">The full path where the temporary file will be created.</param>
    /// <returns>A new <see cref="TemporaryFile"/> instance.</returns>
    /// <remarks>
    /// The file must not already exist. An existing file is never adopted, so disposing the returned instance
    /// cannot delete data that it did not create.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is empty.</exception>
    /// <exception cref="IOException">The file already exists.</exception>
    public static TemporaryFile Create(FullPath filePath)
    {
        if (filePath.IsEmpty)
            throw new ArgumentException("The path is empty.", nameof(filePath));

        CreateFile(filePath);
        return new TemporaryFile(filePath, ownedDirectory: default);
    }

    private static TemporaryFile CreateInRoot(FullPath rootDirectory, string relativePath, FullPath ownedDirectory)
    {
        var fullPath = FullPath.Combine(rootDirectory, relativePath);
        CreateFile(fullPath);
        return new TemporaryFile(fullPath, ownedDirectory);
    }

    private static string CreateUniqueFolderName()
    {
        return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
    }

    private static void CreateFile(FullPath filePath)
    {
        TemporaryPaths.CreateFile(filePath);
    }

    /// <summary>Deletes the temporary file.</summary>
    /// <remarks>
    /// When the instance was created from a file name, the unique directory created to hold that file is deleted as well.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownedDirectory.IsEmpty)
        {
            IOUtilities.Delete(new FileInfo(FullPath));
        }
        else
        {
            IOUtilities.Delete(new DirectoryInfo(_ownedDirectory));
        }
    }

    /// <summary>Asynchronously deletes the temporary file.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    /// <remarks>
    /// When the instance was created from a file name, the unique directory created to hold that file is deleted as well.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        if (_ownedDirectory.IsEmpty)
            return IOUtilities.DeleteAsync(new FileInfo(FullPath), CancellationToken.None);

        return IOUtilities.DeleteAsync(new DirectoryInfo(_ownedDirectory), CancellationToken.None);
    }

    /// <summary>Implicitly converts a <see cref="TemporaryFile"/> to a <see cref="FullPath"/>.</summary>
    /// <param name="temporaryFile">The temporary file to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryFile"/> is <see langword="null"/>.</exception>
    public static implicit operator FullPath(TemporaryFile temporaryFile)
    {
        ArgumentNullException.ThrowIfNull(temporaryFile);
        return temporaryFile.FullPath;
    }

    /// <summary>Implicitly converts a <see cref="TemporaryFile"/> to a string path.</summary>
    /// <param name="temporaryFile">The temporary file to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryFile"/> is <see langword="null"/>.</exception>
    [SuppressMessage("FullPath", "MFFP0011:Return FullPath instead of string", Justification = "There is another implicit operator to convert to FullPath")]
    public static implicit operator string(TemporaryFile temporaryFile)
    {
        ArgumentNullException.ThrowIfNull(temporaryFile);
        return temporaryFile.FullPath;
    }

    /// <summary>Implicitly converts a <see cref="TemporaryFile"/> to a <see cref="FileInfo"/>.</summary>
    /// <param name="temporaryFile">The temporary file to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryFile"/> is <see langword="null"/>.</exception>
    public static implicit operator FileInfo(TemporaryFile temporaryFile)
    {
        ArgumentNullException.ThrowIfNull(temporaryFile);
        return new FileInfo(temporaryFile.FullPath);
    }
}
