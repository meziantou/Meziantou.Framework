using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using Windows.Win32;

namespace Meziantou.Framework;

/// <summary>Represents an absolute file or directory path with convenient path manipulation methods.</summary>
/// <example>
/// <code>
/// // Create FullPath
/// FullPath rootPath = FullPath.FromPath("demo");
/// FullPath filePath = rootPath / "temp" / "file.txt";
///
/// // Compare paths (case-sensitive on Linux, case-insensitive on Windows)
/// bool areEqual = filePath == rootPath;
///
/// // Get relative path
/// string relativePath = filePath.MakePathRelativeTo(rootPath); // temp\file.txt
/// </code>
/// </example>
[JsonConverter(typeof(FullPathJsonConverter))]
public readonly partial struct FullPath : IEquatable<FullPath>, IComparable<FullPath>, IComparable
{
    internal readonly string? _value;

    private FullPath(string path)
    {
        // The checks are already performed in the static methods
        // No need to check if the path is null or absolute here
        Debug.Assert(path is not null);
        Debug.Assert(Path.IsPathFullyQualified(path));
        Debug.Assert(Path.GetFullPath(path) == path);
        _value = path;
    }

    /// <summary>Gets an empty FullPath.</summary>
    public static FullPath Empty => default;

    /// <summary>Gets a value indicating whether this path is empty.</summary>
    [MemberNotNullWhen(returnValue: false, nameof(_value))]
    public bool IsEmpty => _value is null;

    /// <summary>Gets the string representation of the path, or an empty string if the path is empty.</summary>
    /// <remarks>
    /// <para>If the path contains a reserved device name (CON, PRN, AUX, NUL, COM0-COM9, COM¹-COM³, LPT0-LPT9, LPT¹-LPT³),
    /// the extended path format (<c>\\?\</c>) is returned to bypass Win32 namespace restrictions.</para>
    /// <para>Use <see cref="RawValue"/> if you need the unmodified path without device name protection.</para>
    /// </remarks>
    public string Value
    {
        get
        {
            if (_value is null)
                return "";

            if (OperatingSystem.IsWindows() && ContainsReservedDeviceName(_value))
                return PathInternal.EnsureExtendedPrefix(_value);

            return _value;
        }
    }

    /// <summary>Gets the string representation of the path without any conversion, or an empty string if the path is empty.</summary>
    /// <remarks>
    /// <para>Unlike <see cref="Value"/>, this property returns the path exactly as stored internally, without applying
    /// any protection for Windows reserved device names.</para>
    /// <para>Use this property when you need the raw path for operations that handle reserved device names themselves,
    /// or when you're certain the path doesn't contain reserved device names.</para>
    /// </remarks>
    public string RawValue => _value ?? "";

    private static bool ContainsReservedDeviceName(string path)
    {
        var span = path.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            var separatorIndex = span[index..].IndexOfAny('\\', '/');
            var componentEnd = separatorIndex >= 0 ? index + separatorIndex : span.Length;
            var component = span[index..componentEnd];

            if (IsReservedDeviceName(component))
                return true;

            if (separatorIndex < 0)
                break;

            index = componentEnd + 1;
        }

        return false;
    }

    private static bool IsReservedDeviceName(ReadOnlySpan<char> component)
    {
        if (component.IsEmpty)
            return false;

        var dotIndex = component.IndexOf('.');
        var nameOnly = dotIndex >= 0 ? component[..dotIndex] : component;

        if (nameOnly.Length < 3 || nameOnly.Length > 4)
            return false;

        if (nameOnly.Length == 3)
        {
            return nameOnly.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                   nameOnly.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                   nameOnly.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                   nameOnly.Equals("NUL", StringComparison.OrdinalIgnoreCase);
        }

        if (nameOnly.Length == 4)
        {
            if (!nameOnly[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) &&
                !nameOnly[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase))
                return false;

            // Windows documents COM1-COM9 and LPT1-LPT9, but COM0 and LPT0 are accepted here as well.
            // Adding the extended prefix to a path that turns out not to be a device name is harmless,
            // whereas omitting it for an actual device name is not.
            var lastChar = nameOnly[3];
            return lastChar is >= '0' and <= '9' or '\u00B9' or '\u00B2' or '\u00B3';
        }

        return false;
    }

    /// <summary>Implicitly converts a <see cref="FullPath"/> to a <see cref="string"/>.</summary>
    public static implicit operator string(FullPath fullPath) => fullPath.ToString();

    public static bool operator ==(FullPath path1, FullPath path2) => path1.Equals(path2);
    public static bool operator !=(FullPath path1, FullPath path2) => !(path1 == path2);
    public static bool operator <(FullPath path1, FullPath path2) => path1.CompareTo(path2) < 0;
    public static bool operator >(FullPath path1, FullPath path2) => path1.CompareTo(path2) > 0;
    public static bool operator <=(FullPath path1, FullPath path2) => path1.CompareTo(path2) <= 0;
    public static bool operator >=(FullPath path1, FullPath path2) => path1.CompareTo(path2) >= 0;

    /// <summary>Combines a root path with a relative path using the / operator.</summary>
    public static FullPath operator /(FullPath rootPath, string relativePath) => Combine(rootPath, relativePath);

    /// <summary>Combines a root path with a path using the + operator, ensuring that string concatenation occurs before FullPath concatenation.</summary>
    public static FullPath operator +(FullPath rootPath, string suffix) => FromPath(rootPath.Value + suffix);

    /// <summary>Gets the parent directory of this path, or <see cref="Empty"/> if there is no parent.</summary>
    public FullPath Parent
    {
        get
        {
            var result = Path.GetDirectoryName(_value);
            if (result is null)
                return Empty;

            return new FullPath(result);
        }
    }

    /// <summary>Gets the file or directory name and extension.</summary>
    public string Name => Path.GetFileName(_value) ?? "";

    /// <summary>Gets the file or directory name without the extension.</summary>
    public string NameWithoutExtension => Path.GetFileNameWithoutExtension(_value) ?? "";

    /// <summary>Gets the file extension including the leading dot.</summary>
    public string Extension => Path.GetExtension(_value) ?? "";

    /// <summary>Compares this path to another using the default comparer for the current operating system.</summary>
    public int CompareTo(FullPath other) => FullPathComparer.Default.Compare(this, other);

    /// <summary>Compares this path to another with optional case-insensitive comparison.</summary>
    public int CompareTo(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Compare(this, other);

    int IComparable.CompareTo(object? obj)
    {
        if (obj is null)
            return 1;

        if (obj is FullPath other)
            return CompareTo(other);

        throw new ArgumentException($"Object must be of type {nameof(FullPath)}", nameof(obj));
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FullPath path && Equals(path);
    public bool Equals(FullPath other) => FullPathComparer.Default.Equals(this, other);

    /// <summary>Determines whether this path equals another path with optional case-insensitive comparison.</summary>
    public bool Equals(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Equals(this, other);

    public override int GetHashCode() => FullPathComparer.Default.GetHashCode(this);

    /// <summary>Returns a hash code for this path with optional case-insensitive comparison.</summary>
    public int GetHashCode(bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).GetHashCode(this);

    public override string ToString() => Value;

    /// <summary>Creates a relative path from this path to the specified root path.</summary>
    /// <param name="rootPath">The root path to make this path relative to.</param>
    /// <returns>A relative path string, or <c>"."</c> when both paths are equal.</returns>
    public string MakePathRelativeTo(FullPath rootPath)
    {
        if (IsEmpty)
            throw new InvalidOperationException("The path is empty");

        if (rootPath.IsEmpty)
            return _value;

        return Path.GetRelativePath(rootPath._value, _value);
    }

    /// <summary>Determines whether this path is a child of the specified root path.</summary>
    /// <param name="rootPath">The root path to check against.</param>
    /// <returns><see langword="true"/> if this path is a child of the root path; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The comparison uses the same case sensitivity as <see cref="FullPathComparer.Default"/>.</remarks>
    public bool IsChildOf(FullPath rootPath)
    {
        if (IsEmpty)
            throw new InvalidOperationException("Path is empty");
        if (rootPath.IsEmpty)
            throw new ArgumentException("Root path is empty", nameof(rootPath));

        var root = rootPath._value;
        if (_value.Length <= root.Length)
            return false;

        var comparison = FullPathComparer.Default.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!_value.StartsWith(root, comparison))
            return false;

        // Root directories such as "/" or "C:\" keep their trailing separator, so the child does not have an extra one
        // rootpath: /
        // current:  /a    => true
        if (root[^1] == Path.DirectorySeparatorChar)
            return true;

        // rootpath: /a/b
        // current:  /a/b/c => true
        // current:  /a/b/  => false
        // current:  /a/bc  => false
        return _value[root.Length] == Path.DirectorySeparatorChar && _value.Length > root.Length + 1;
    }

    /// <summary>Creates the parent directory of this path if it doesn't exist.</summary>
    public void CreateParentDirectory()
    {
        if (IsEmpty)
            return;

        var parent = Path.GetDirectoryName(Value);
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }
    }

    // Old names to prevent breaking changes, but hidden from IntelliSense to encourage using the new names (WithXyz)
    [EditorBrowsable(EditorBrowsableState.Never)]
    public FullPath ChangeExtension(string? extension) => WithExtension(extension);

    /// <summary>Returns a new path with the specified file extension.</summary>
    /// <param name="extension">The new extension (with or without the leading dot), or <see langword="null"/> to remove the extension.</param>
    public FullPath WithExtension(string? extension)
    {
        if (IsEmpty)
            return Empty;

        return new FullPath(Path.ChangeExtension(_value, extension));
    }

    /// <summary>Returns a new path with the specified file extension, optionally replacing all trailing extensions.</summary>
    /// <param name="extension">The new extension (with or without the leading dot), or <see langword="null"/> to remove the extension.</param>
    /// <param name="replaceAllTrailingExtensions"><see langword="true"/> to replace all trailing extensions; <see langword="false"/> to replace only the last extension.</param>
    /// <returns>A new <see cref="FullPath"/> instance with the specified extension changes.</returns>
    public FullPath WithExtension(string? extension, bool replaceAllTrailingExtensions)
    {
        return replaceAllTrailingExtensions ? WithExtension(extension, int.MaxValue) : WithExtension(extension);
    }

    /// <summary>Returns a new path with the specified file extension, replacing a specific number of trailing extensions.</summary>
    /// <param name="extension">The new extension (with or without the leading dot), or <see langword="null"/> to remove the extension.</param>
    /// <param name="extensionCount">The number of trailing extensions to replace. Must be greater than 0.</param>
    /// <returns>A new <see cref="FullPath"/> instance with the specified extension changes.</returns>
    public FullPath WithExtension(string? extension, int extensionCount)
    {
        if (extensionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(extensionCount), extensionCount, "Value must be greater than 0.");

        if (extensionCount is 1)
            return WithExtension(extension);

        if (IsEmpty)
            return Empty;

        var current = _value;
        var extensionsRemoved = 0;
        while (true)
        {
            var ext = Path.GetExtension(current);
            if (string.IsNullOrEmpty(ext))
                break;

            current = current[..^ext.Length];
            extensionsRemoved++;

            if (extensionsRemoved >= extensionCount)
                break;
        }

        if (string.IsNullOrEmpty(extension))
            return new FullPath(current);

        if (!extension.StartsWith('.', StringComparison.Ordinal))
            extension = "." + extension;

        return new FullPath(current + extension);
    }

    /// <summary>Returns a new path with the specified name, keeping the same parent directory.</summary>
    /// <param name="name">The new name for the path.</param>
    /// <returns>A new <see cref="FullPath"/> instance with the specified name.</returns>
    /// <remarks>If the current path is empty, the returned path will also be empty.</remarks>
    public FullPath WithName(string name)
    {
        if (IsEmpty)
            return Empty;

        var parent = Path.GetDirectoryName(_value);
        if (parent is null)
            return new FullPath(name);

        return new FullPath(Path.Combine(parent, name));
    }

    /// <summary>Returns a new path with the specified name, keeping the same parent directory but without changing the extension.</summary>
    /// <param name="nameWithoutExtension">The new name for the path, without the extension.</param>
    /// <returns>A new <see cref="FullPath"/> instance with the specified name.</returns>
    /// <remarks>If the current path is empty, the returned path will also be empty.</remarks>
    public FullPath WithNameWithoutExtension(string nameWithoutExtension)
    {
        if (IsEmpty)
            return Empty;

        var parent = Path.GetDirectoryName(_value);
        var extension = Path.GetExtension(_value);
        var newName = nameWithoutExtension + extension;
        if (parent is null)
            return new FullPath(newName);

        return new FullPath(Path.Combine(parent, newName));
    }

    /// <summary>Gets the path of the system's temporary folder.</summary>
    public static FullPath GetTempPath() => FromPath(Path.GetTempPath());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static FullPath GetTempFileName() => CreateTempFile();

    /// <summary>Creates a uniquely named, zero-byte temporary file and returns its full path.</summary>
    public static FullPath CreateTempFile() => CreateTempFile(prefix: null);

    /// <summary>Creates a uniquely named, zero-byte temporary file and returns its full path.</summary>
    /// <param name="prefix">A prefix to prepend to the generated file name.</param>
    /// <param name="suffix">A suffix to append to the generated file name. Defaults to <c>.tmp</c>.</param>
    /// <exception cref="IOException">Thrown when a unique file could not be created after 10 attempts.</exception>
    public static FullPath CreateTempFile(string? prefix, string? suffix = ".tmp") => CreateTempFile(folder: null, prefix: prefix, suffix: suffix);

    /// <summary>Creates a uniquely named, zero-byte temporary file and returns its full path.</summary>
    /// <param name="folder">The destination folder. If <see langword="null"/> or empty, the system temporary folder is used.</param>
    /// <param name="prefix">A prefix to prepend to the generated file name.</param>
    /// <param name="suffix">A suffix to append to the generated file name. Defaults to <c>.tmp</c>.</param>
    /// <exception cref="IOException">Thrown when a unique file could not be created after 10 attempts.</exception>
    public static FullPath CreateTempFile(FullPath? folder, string? prefix, string? suffix = ".tmp")
    {
        var destinationFolder = folder.GetValueOrDefault();
        if (destinationFolder.IsEmpty)
        {
            destinationFolder = GetTempPath();
        }

        Directory.CreateDirectory(destinationFolder.Value);

        prefix ??= string.Empty;
        suffix ??= string.Empty;

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
        {
            // FileStream would otherwise create the file with 0666 & ~umask, which leaves it readable by every local
            // user when the temporary folder is shared (Path.GetTempPath is /tmp on most Linux systems).
            // Path.GetTempFileName uses mkstemp and creates with 0600, so match that.
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        IOException? lastException = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var filePath = destinationFolder / (prefix + Guid.NewGuid().ToString("N") + suffix);

            try
            {
                using var stream = File.Open(filePath.Value, options);
                return filePath;
            }
            catch (IOException ex)
            {
                lastException = ex;
            }
        }

        throw new IOException("Could not create a unique temporary file after 10 attempts.", lastException);
    }

    /// <summary>Gets the path to the system special folder identified by the specified enumeration.</summary>
    /// <param name="folder">The special folder to retrieve the path for.</param>
    public static FullPath GetFolderPath(Environment.SpecialFolder folder) => FromPath(Environment.GetFolderPath(folder));

    /// <summary>Gets the path to the system special folder identified by the specified enumeration.</summary>
    /// <param name="folder">The special folder to retrieve the path for.</param>
    /// <param name="option">Specifies whether the folder must be verified or created.</param>
    public static FullPath GetFolderPath(Environment.SpecialFolder folder, Environment.SpecialFolderOption option) => FromPath(Environment.GetFolderPath(folder, option));

    /// <summary>Gets the path to a Windows known folder.</summary>
    /// <param name="knownFolder">The known folder to retrieve the path for.</param>
    [SupportedOSPlatform("windows6.0.6000")]
    public static unsafe FullPath GetKnownFolderPath(KnownFolder knownFolder)
    {
        var result = PInvoke.SHGetKnownFolderPath(knownFolder.FolderId, Windows.Win32.UI.Shell.KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, hToken: null, out var path);
        if (result.Succeeded)
        {
            var expandedValue = Environment.ExpandEnvironmentVariables(path.ToString());
            Marshal.FreeCoTaskMem((nint)path.Value);
            return FromPath(expandedValue);
        }

        Marshal.FreeCoTaskMem((nint)path.Value);
        throw new Win32Exception(result.Value, $"Failed to get shell folder path for {knownFolder}");
    }

    /// <summary>Gets the current working directory.</summary>
    public static FullPath CurrentDirectory() => FromPath(Environment.CurrentDirectory);

    /// <summary>Creates a <see cref="FullPath"/> from a string path by converting it to an absolute path.</summary>
    /// <param name="path">The path to convert. Can be relative or absolute.</param>
    public static FullPath FromPath(string path)
    {
        // '\' is a regular file name character on Unix, so a path such as @"\\?\a" is a relative file name there, not a device path
        if (OperatingSystem.IsWindows() && PathInternal.IsExtended(path))
        {
            path = path[PathInternal.DevicePrefixLength..];
        }

        var fullPath = Path.GetFullPath(path);
        var fullPathWithoutTrailingDirectorySeparator = Path.TrimEndingDirectorySeparator(fullPath);
        if (string.IsNullOrEmpty(fullPathWithoutTrailingDirectorySeparator))
            return Empty;

        return new FullPath(fullPathWithoutTrailingDirectorySeparator);
    }

    /// <summary>Combines two path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string relativePath) => FromPath(Path.Combine(rootPath, relativePath));

    /// <summary>Combines three path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string path1, string path2) => FromPath(Path.Combine(rootPath, path1, path2));

    /// <summary>Combines four path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string path1, string path2, string path3) => FromPath(Path.Combine(rootPath, path1, path2, path3));

    /// <summary>Combines an array of path strings into a full path.</summary>
    public static FullPath Combine(params string[] paths) => FromPath(Path.Combine(paths));

    /// <summary>Combines a span of path strings into a full path.</summary>
    public static FullPath Combine(params ReadOnlySpan<string> paths) => FromPath(Path.Combine(paths));

    /// <summary>Combines a <see cref="FullPath"/> with a relative path.</summary>
    public static FullPath Combine(FullPath rootPath, string relativePath)
    {
        if (rootPath.IsEmpty)
            return FromPath(relativePath);

        return FromPath(Path.Combine(rootPath._value, relativePath));
    }

    /// <summary>Combines a <see cref="FullPath"/> with two relative paths.</summary>
    public static FullPath Combine(FullPath rootPath, string path1, string path2)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(path1, path2));

        return FromPath(Path.Combine(rootPath._value, path1, path2));
    }

    /// <summary>Combines a <see cref="FullPath"/> with multiple relative paths.</summary>
    public static FullPath Combine(FullPath rootPath, params string[] paths)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(paths));

        return FromPath(Path.Combine(rootPath._value, Path.Combine(paths)));
    }

    /// <summary>Combines a <see cref="FullPath"/> with a span of relative paths.</summary>
    public static FullPath Combine(FullPath rootPath, params ReadOnlySpan<string> paths)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(paths));

        return FromPath(Path.Combine([rootPath._value, .. paths]));
    }

    /// <summary>Combines a <see cref="FullPath"/> with three relative paths.</summary>
    public static FullPath Combine(FullPath rootPath, string path1, string path2, string path3)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(path1, path2, path3));

        return FromPath(Path.Combine(rootPath._value, path1, path2, path3));
    }

    /// <summary>Creates a <see cref="FullPath"/> from a <see cref="FileSystemInfo"/> object.</summary>
    public static FullPath FromFileSystemInfo(FileSystemInfo? fsi)
    {
        if (fsi is null)
            return Empty;

        return FromPath(fsi.FullName);
    }

    // Matches the number of levels the operating systems themselves are willing to follow (MAXSYMLINKS on Linux)
    private const int MaxSymbolicLinkDepth = 40;

    private static IOException CreateTooManyLevelsOfSymbolicLinksException()
    {
        return new IOException($"Too many levels of symbolic links (more than {MaxSymbolicLinkDepth.ToString(CultureInfo.InvariantCulture)})");
    }

    /// <summary>Determines whether this path represents a symbolic link.</summary>
    public bool IsSymbolicLink()
    {
        if (IsEmpty)
            return false;

        return Symlink.IsSymbolicLink(_value);
    }

    /// <summary>Attempts to resolve this path to its canonical final existing path.</summary>
    /// <param name="result">The canonical final path if successful; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if canonical resolution succeeds; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>The canonical path resolves symbolic links and reparse points to the final target.</para>
    /// <para>This method returns <see langword="false"/> when the path does not exist or cannot be resolved.</para>
    /// </remarks>
    public bool TryGetCanonicalPath([NotNullWhen(true)] out FullPath? result)
    {
        if (!IsEmpty && CanonicalPath.TryGetCanonicalPath(_value, out var path))
        {
            result = FromPath(path);
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Finds the first ancestor path or self that matches the specified predicate.</summary>
    /// <param name="predicate">A function to test each path.</param>
    /// <param name="result">The first matching path, or default if not found.</param>
    /// <returns><see langword="true"/> if a matching path is found; otherwise, <see langword="false"/>.</returns>
    public bool TryFindFirstAncestorOrSelf(Func<FullPath, bool> predicate, out FullPath result)
    {
        var current = this;
        while (!current.IsEmpty)
        {
            if (predicate(current))
            {
                result = current;
                return true;
            }

            current = current.Parent;
        }

        result = default;
        return false;
    }

    /// <summary>Finds the first ancestor path or self that contains a Git repository.</summary>
    /// <param name="result">The first matching path, or default if not found.</param>
    /// <returns><see langword="true"/> if a Git repository is found; otherwise, <see langword="false"/>.</returns>
    public bool TryFindGitRepositoryRoot(out FullPath result)
    {
        var start = this;
        if (!start.IsEmpty && File.Exists(start._value))
        {
            start = start.Parent;
        }

        return start.TryFindFirstAncestorOrSelf(path =>
        {
            var gitPath = path / ".git";
            return Directory.Exists(gitPath) || File.Exists(gitPath);
        }, out result);
    }

    /// <summary>Finds the first ancestor path or self that contains a Git repository.</summary>
    /// <returns>The first matching path.</returns>
    /// <exception cref="InvalidOperationException">The Git repository cannot be found.</exception>
    public FullPath FindRequiredGitRepositoryRoot()
    {
        if (TryFindGitRepositoryRoot(out var result))
            return result;

        throw new InvalidOperationException("Git repository not found.");
    }

    /// <summary>Finds the first ancestor path (excluding self) that matches the specified predicate.</summary>
    /// <param name="predicate">A function to test each ancestor path.</param>
    /// <param name="result">The first matching ancestor path, or default if not found.</param>
    /// <returns><see langword="true"/> if a matching ancestor is found; otherwise, <see langword="false"/>.</returns>
    public bool TryFindFirstAncestor(Func<FullPath, bool> predicate, out FullPath result)
    {
        return Parent.TryFindFirstAncestorOrSelf(predicate, out result);
    }

    /// <summary>Attempts to get the immediate target of a symbolic link.</summary>
    /// <param name="result">The target path if this is a symbolic link; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if this is a symbolic link; otherwise, <see langword="false"/>.</returns>
    public bool TryGetSymbolicLinkTarget([NotNullWhen(true)] out FullPath? result)
    {
        return TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out result);
    }

    /// <summary>Attempts to get the target of a symbolic link using the specified resolution mode.</summary>
    /// <param name="resolutionMode">The mode to use when resolving symbolic links.</param>
    /// <param name="result">The target path if this is a symbolic link; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if this is a symbolic link; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="IOException">A symbolic link chain is too deep to resolve, which usually means the links form a cycle.</exception>
    public bool TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode resolutionMode, [NotNullWhen(true)] out FullPath? result)
    {
        if (!IsEmpty)
        {
            switch (resolutionMode)
            {
                case SymbolicLinkResolutionMode.Immediate:
                    if (Symlink.TryGetSymLinkTarget(_value, out var path))
                    {
                        result = FromPath(path);
                        return true;
                    }

                    break;

                case SymbolicLinkResolutionMode.FinalTarget:
                    var value = _value;
                    var depth = 0;
                    while (Symlink.TryGetSymLinkTarget(value, out path))
                    {
                        if (++depth > MaxSymbolicLinkDepth)
                            throw CreateTooManyLevelsOfSymbolicLinksException();

                        value = path;
                    }

                    if (value != _value)
                    {
                        result = FromPath(value);
                        return true;
                    }

                    break;

                case SymbolicLinkResolutionMode.AllSymbolicLinks:
                    string? resultPath = null;
                    var current = this;
                    var hasSymLink = false;
                    var componentDepth = 0;
                    while (!current.IsEmpty)
                    {
                        if (Symlink.TryGetSymLinkTarget(current._value, out path))
                        {
                            if (++componentDepth > MaxSymbolicLinkDepth)
                                throw CreateTooManyLevelsOfSymbolicLinksException();

                            current = FromPath(path);
                            hasSymLink = true;
                        }
                        else
                        {
                            var name = current.Name is "" ? current._value : current.Name;
                            if (resultPath is null)
                            {
                                resultPath = name;
                            }
                            else
                            {
                                resultPath = Path.Combine(name, resultPath);
                            }

                            current = current.Parent;
                            componentDepth = 0;
                        }
                    }

                    if (hasSymLink)
                    {
                        result = FromPath(resultPath!);
                        return true;
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(resolutionMode));
            }
        }

        result = null;
        return false;
    }

    /// <summary>Opens the system file manager and selects this file or directory.</summary>
    [SupportedOSPlatform("windows5.1.2600")]
    [SupportedOSPlatform("macos")]
    public unsafe void OpenInExplorer()
    {
        if (IsEmpty)
            throw new InvalidOperationException("Path is empty");

        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
        {
            var itemList = PInvoke.ILCreateFromPath(Value);
            if (itemList is not null)
            {
                try
                {
                    PInvoke.SHOpenFolderAndSelectItems(itemList, 0u, apidl: null, 0u).ThrowOnFailure();
                }
                finally
                {
                    PInvoke.ILFree(itemList);
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var processStartInfo = new ProcessStartInfo("/usr/bin/open");
            processStartInfo.ArgumentList.Add("-R");
            processStartInfo.ArgumentList.Add(Value);
            using var process = Process.Start(processStartInfo);
        }
        else
        {
            throw new PlatformNotSupportedException("Opening the system file manager is only supported on Windows 5.1.2600 and later, and macOS.");
        }
    }

    /// <summary>Converts this path to Windows extended-length path format (<c>\\?\</c>).</summary>
    /// <returns>The path in extended-length format, or an empty string if the path is empty.</returns>
    /// <remarks>
    /// <para>The extended-length path format bypasses most path parsing and validation, allowing paths longer than 260 characters.</para>
    /// <para>UNC paths are converted to <c>\\?\UNC\server\share</c> format.</para>
    /// <para>If the path already uses a device path syntax (<c>\\?\</c>, <c>\\.\</c>, or <c>\??\</c>), it is returned as-is.</para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public string ToWindowsExtendedPath()
    {
        if (IsEmpty)
            return "";

        return PathInternal.EnsureExtendedPrefix(_value);
    }
}
