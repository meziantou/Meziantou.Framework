using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>Represents a temporary directory that is automatically deleted when disposed.</summary>
/// <example>
/// <code>
/// // Basic usage with using statement
/// using var tempDir = TemporaryDirectory.Create();
/// var filePath = tempDir.CreateTextFile("test.txt", "Hello World");
/// Console.WriteLine($"File created at: {filePath}");
/// // Directory is automatically deleted when disposed
///
/// // Async disposal
/// await using var tempDir2 = TemporaryDirectory.Create();
/// await tempDir2.CreateTextFileAsync("data.json", "{}");
///
/// // Working with subdirectories
/// using var tempDir3 = TemporaryDirectory.Create();
/// tempDir3.CreateDirectory("subdir");
/// var nestedFile = tempDir3.GetFullPath("subdir/file.txt");
/// File.WriteAllText(nestedFile, "content");
///
/// // Using path operators
/// using var tempDir4 = TemporaryDirectory.Create();
/// var path = tempDir4 / "data" / "output.txt";
/// </code>
/// </example>
/// <remarks>
/// This class creates a unique temporary directory that is automatically cleaned up when disposed.
/// Uniqueness is guaranteed by including a GUID in the directory name.
/// </remarks>
[DebuggerDisplay("{FullPath}")]
public sealed class TemporaryDirectory : IDisposable, IAsyncDisposable
{
    /// <summary>Gets the full path to the temporary directory.</summary>
    /// <value>The absolute path to the temporary directory where files and subdirectories can be created.</value>
    public FullPath FullPath { get; }

    private TemporaryDirectory(FullPath path)
    {
        FullPath = path;
    }

    /// <summary>Creates a new temporary directory in the system's default temp location.</summary>
    /// <returns>A new <see cref="TemporaryDirectory"/> instance.</returns>
    /// <remarks>
    /// The directory is created under the system temp path in a "MezTD" subdirectory.
    /// The directory name includes a timestamp and GUID to ensure uniqueness.
    /// </remarks>
    public static TemporaryDirectory Create()
    {
        return Create(FullPath.Combine(Path.GetTempPath(), "MezTD"));
    }

    /// <summary>Creates a new temporary directory under the specified root directory.</summary>
    /// <param name="rootDirectory">The root directory where the temporary directory will be created.</param>
    /// <returns>A new <see cref="TemporaryDirectory"/> instance.</returns>
    /// <remarks>
    /// The directory name includes a timestamp (yyyyMMdd_HHmmss) and GUID to ensure uniqueness.
    /// </remarks>
    public static TemporaryDirectory Create(FullPath rootDirectory)
    {
        var folderName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
        var path = CreateUniqueDirectory(rootDirectory / folderName);
        return new TemporaryDirectory(path);
    }

    /// <summary>Gets the full path for a relative path within the temporary directory.</summary>
    /// <param name="relativePath">The relative path (can include subdirectories).</param>
    /// <returns>The absolute <see cref="FullPath"/> combining the temporary directory and the relative path.</returns>
    /// <example>
    /// <code>
    /// using var tempDir = TemporaryDirectory.Create();
    /// var fullPath = tempDir.GetFullPath("data/output.txt");
    /// </code>
    /// </example>
    public FullPath GetFullPath(string relativePath)
    {
        return FullPath.Combine(FullPath, relativePath);
    }

    /// <summary>Creates an empty file at the specified relative path.</summary>
    /// <param name="relativePath">The relative path for the file (can include subdirectories).</param>
    /// <returns>The absolute <see cref="FullPath"/> of the created file.</returns>
    /// <remarks>If the parent directories don't exist, they will be created automatically.</remarks>
    /// <example>
    /// <code>
    /// using var tempDir = TemporaryDirectory.Create();
    /// var filePath = tempDir.CreateEmptyFile("test.txt");
    /// </code>
    /// </example>
    public FullPath CreateEmptyFile(string relativePath)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        return path;
    }

    /// <summary>Creates a text file with the specified content at the relative path.</summary>
    /// <param name="relativePath">The relative path for the file (can include subdirectories).</param>
    /// <param name="content">The text content to write to the file.</param>
    /// <returns>The absolute <see cref="FullPath"/> of the created file.</returns>
    /// <remarks>If the parent directories don't exist, they will be created automatically.</remarks>
    /// <example>
    /// <code>
    /// using var tempDir = TemporaryDirectory.Create();
    /// var filePath = tempDir.CreateTextFile("config.json", "{ \"setting\": \"value\" }");
    /// </code>
    /// </example>
    public FullPath CreateTextFile(string relativePath, string content)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Asynchronously creates a text file with the specified content at the relative path.</summary>
    /// <param name="relativePath">The relative path for the file (can include subdirectories).</param>
    /// <param name="content">The text content to write to the file.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the absolute <see cref="FullPath"/> of the created file.</returns>
    /// <remarks>If the parent directories don't exist, they will be created automatically.</remarks>
    /// <example>
    /// <code>
    /// await using var tempDir = TemporaryDirectory.Create();
    /// var filePath = await tempDir.CreateTextFileAsync("data.txt", "Hello World");
    /// </code>
    /// </example>
    public async Task<FullPath> CreateTextFileAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path.Parent);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Creates a directory at the specified relative path.</summary>
    /// <param name="relativePath">The relative path for the directory (can include subdirectories).</param>
    /// <returns>The absolute <see cref="FullPath"/> of the created directory.</returns>
    /// <remarks>If parent directories don't exist, they will be created automatically.</remarks>
    /// <example>
    /// <code>
    /// using var tempDir = TemporaryDirectory.Create();
    /// var dirPath = tempDir.CreateDirectory("data/output");
    /// </code>
    /// </example>
    public FullPath CreateDirectory(string relativePath)
    {
        var path = GetFullPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static FullPath CreateUniqueDirectory(FullPath folderPath)
    {
        // The folder name includes a GUID, so it is unique and cannot collide with an existing directory.
        Directory.CreateDirectory(folderPath);
        return folderPath;
    }

    /// <summary>Deletes the temporary directory and all its contents.</summary>
    public void Dispose()
    {
        IOUtilities.Delete(new DirectoryInfo(FullPath));
    }

    /// <summary>Asynchronously deletes the temporary directory and all its contents.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await IOUtilities.DeleteAsync(new DirectoryInfo(FullPath), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Combines the temporary directory path with a relative path using the / operator.</summary>
    /// <param name="temporaryDirectory">The temporary directory.</param>
    /// <param name="path">The relative path to combine.</param>
    /// <returns>The combined <see cref="FullPath"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryDirectory"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// using var tempDir = TemporaryDirectory.Create();
    /// var filePath = tempDir / "data" / "output.txt";
    /// </code>
    /// </example>
    public static FullPath operator /(TemporaryDirectory temporaryDirectory, string path)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.GetFullPath(path);
    }

    /// <summary>Implicitly converts a <see cref="TemporaryDirectory"/> to a <see cref="FullPath"/>.</summary>
    /// <param name="temporaryDirectory">The temporary directory to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryDirectory"/> is <see langword="null"/>.</exception>
    public static implicit operator FullPath(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.FullPath;
    }

    /// <summary>Implicitly converts a <see cref="TemporaryDirectory"/> to a string path.</summary>
    /// <param name="temporaryDirectory">The temporary directory to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryDirectory"/> is <see langword="null"/>.</exception>
    [SuppressMessage("FullPath", "MFFP0011:Return FullPath instead of string", Justification = "There is another implicit operator to convert to FullPath")]
    public static implicit operator string(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return temporaryDirectory.FullPath;
    }

    /// <summary>Implicitly converts a <see cref="TemporaryDirectory"/> to a <see cref="DirectoryInfo"/>.</summary>
    /// <param name="temporaryDirectory">The temporary directory to convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="temporaryDirectory"/> is <see langword="null"/>.</exception>
    public static implicit operator DirectoryInfo(TemporaryDirectory temporaryDirectory)
    {
        ArgumentNullException.ThrowIfNull(temporaryDirectory);
        return new DirectoryInfo(temporaryDirectory.FullPath);
    }

    /// <summary>Opens the temporary directory in Windows Explorer.</summary>
    /// <remarks>This method is only available on Windows platforms.</remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows5.1.2600")]
    public void OpenInExplorer() => FullPath.OpenInExplorer();
}
