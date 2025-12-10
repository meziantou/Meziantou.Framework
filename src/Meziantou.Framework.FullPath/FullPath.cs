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
public readonly partial struct FullPath : IEquatable<FullPath>, IComparable<FullPath>
{
    internal readonly string? _value;

    private FullPath(string path)
    {
        // The checks are already performed in the static methods
        // No need to check if the path is null or absolute here
        Debug.Assert(path is not null);
#if NETCOREAPP3_1_OR_GREATER
        Debug.Assert(Path.IsPathFullyQualified(path));
#elif NETSTANDARD2_0 || NET472
#else
#error Platform not supported
#endif
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
    /// If the path contains a reserved device name (CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9),
    /// the extended path format (<c>\\?\</c>) is returned to bypass Win32 namespace restrictions.
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

    public override bool Equals(object? obj) => obj is FullPath path && Equals(path);
    public bool Equals(FullPath other) => FullPathComparer.Default.Equals(this, other);

    /// <summary>Determines whether this path equals another path with optional case-insensitive comparison.</summary>
    public bool Equals(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Equals(this, other);

    public override int GetHashCode() => FullPathComparer.Default.GetHashCode(this);

    /// <summary>Returns a hash code for this path with optional case-insensitive comparison.</summary>
    public int GetHashCode(bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).GetHashCode(this);

    public override string ToString() => Value;

    /// <summary>Creates a relative path from this path to the specified root path.</summary>
    /// <param name="rootPath">The root path to make this path relative to.</param>
    /// <returns>A relative path string.</returns>
    public string MakePathRelativeTo(FullPath rootPath)
    {
        if (IsEmpty)
            throw new InvalidOperationException("The path is empty");

        if (rootPath.IsEmpty)
            return _value;

        if (rootPath == this)
            return ".";

        return PathDifference(rootPath._value, _value, compareCase: FullPathComparer.Default.IsCaseSensitive);
    }

    private static string PathDifference(string path1, string path2, bool compareCase)
    {
        var directorySeparator = Path.DirectorySeparatorChar;

        int i;
        var si = -1;
        for (i = 0; (i <= path1.Length) && (i < path2.Length); ++i)
        {
            var c1 = i == path1.Length ? directorySeparator : path1[i];
            var c2 = path2[i];

            if ((c1 != c2) && (compareCase || (char.ToUpperInvariant(c1) != char.ToUpperInvariant(c2))))
                break;

            if (c1 == directorySeparator)
            {
                si = i;
            }
        }

        if (i == 0)
            return path2;

        if ((i == path1.Length + 1) && (i == path2.Length))
            return "";

        var relPath = new StringBuilder();
        // Walk down several dirs
        for (; i <= path1.Length; ++i)
        {
            var c = i == path1.Length ? directorySeparator : path1[i];
            if (c == directorySeparator)
            {
                relPath.Append("..");
                relPath.Append(directorySeparator);
            }
        }

        return relPath.Append(path2.AsSpan(si + 1)).ToString();
    }

    /// <summary>Determines whether this path is a child of the specified root path.</summary>
    /// <param name="rootPath">The root path to check against.</param>
    /// <returns><see langword="true"/> if this path is a child of the root path; otherwise, <see langword="false"/>.</returns>
    public bool IsChildOf(FullPath rootPath)
    {
        if (IsEmpty)
            throw new InvalidOperationException("Path is empty");
        if (rootPath.IsEmpty)
            throw new ArgumentException("Root path is empty", nameof(rootPath));

        if (_value.Length <= rootPath._value.Length)
            return false;

        if (!_value.StartsWith(rootPath._value, StringComparison.Ordinal))
            return false;

        // rootpath: /a/b
        // current:  /a/b/c => true
        // current:  /a/b/  => false
        // current:  /a/bc  => false
        if (_value[rootPath._value.Length] == Path.DirectorySeparatorChar && _value.Length > rootPath._value.Length + 1)
            return true;

        return false;
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

    /// <summary>Returns a new path with the specified file extension.</summary>
    /// <param name="extension">The new extension (with or without the leading dot), or <see langword="null"/> to remove the extension.</param>
    public FullPath ChangeExtension(string? extension)
    {
        if (IsEmpty)
            return Empty;

        return new FullPath(Path.ChangeExtension(Value, extension));
    }

    /// <summary>Gets the path of the system's temporary folder.</summary>
    public static FullPath GetTempPath() => FromPath(Path.GetTempPath());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static FullPath GetTempFileName() => FromPath(Path.GetTempFileName());

    /// <summary>Creates a uniquely named, zero-byte temporary file and returns its full path.</summary>
    public static FullPath CreateTempFile() => FromPath(Path.GetTempFileName());

    /// <summary>Gets the path to the system special folder identified by the specified enumeration.</summary>
    public static FullPath GetFolderPath(Environment.SpecialFolder folder) => FromPath(Environment.GetFolderPath(folder));

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
        if (PathInternal.IsExtended(path))
        {
            path = path[4..];
        }

        var fullPath = Path.GetFullPath(path);
        var fullPathWithoutTrailingDirectorySeparator = TrimEndingDirectorySeparator(fullPath);
        if (string.IsNullOrEmpty(fullPathWithoutTrailingDirectorySeparator))
            return Empty;

        return new FullPath(fullPathWithoutTrailingDirectorySeparator);
    }

    private static string TrimEndingDirectorySeparator(string path)
    {
#if NETCOREAPP3_1_OR_GREATER
        return Path.TrimEndingDirectorySeparator(path);
#elif NETSTANDARD2_0 || NET472
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) && !path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            return path;

        if (path.StartsWith("\\", StringComparison.Ordinal))
            throw new ArgumentException("UNC paths are not supported", nameof(path));

        return path[0..^1];
#else
#error Platform not supported
#endif
    }

    /// <summary>Combines two path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string relativePath) => FromPath(Path.Combine(rootPath, relativePath));

    /// <summary>Combines three path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string path1, string path2) => FromPath(Path.Combine(rootPath, path1, path2));

    /// <summary>Combines four path strings into a full path.</summary>
    public static FullPath Combine(string rootPath, string path1, string path2, string path3) => FromPath(Path.Combine(rootPath, path1, path2, path3));

    /// <summary>Combines an array of path strings into a full path.</summary>
    public static FullPath Combine(params string[] paths) => FromPath(Path.Combine(paths));

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

#if NET9_0_OR_GREATER
    /// <summary>Combines a <see cref="FullPath"/> with a span of relative paths.</summary>
    public static FullPath Combine(FullPath rootPath, params ReadOnlySpan<string> paths)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(paths));

        return FromPath(Path.Combine([rootPath._value, .. paths]));
    }
#endif

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

    /// <summary>Determines whether this path represents a symbolic link.</summary>
    public bool IsSymbolicLink()
    {
        if (IsEmpty)
            return false;

        return Symlink.IsSymbolicLink(_value);
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
                    while (Symlink.TryGetSymLinkTarget(value, out path))
                    {
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
                    while (!current.IsEmpty)
                    {
                        if (Symlink.TryGetSymLinkTarget(current._value, out path))
                        {
                            current = FromPath(path);
                            hasSymLink = true;
                        }
                        else
                        {
                            var name = current.Name is "" ? current._value : current.Name!;
                            if (resultPath is null)
                            {
                                resultPath = name;
                            }
                            else
                            {
                                resultPath = Path.Combine(name, resultPath);
                            }

                            current = current.Parent;
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

    /// <summary>Opens Windows Explorer and selects this file or directory.</summary>
    [SupportedOSPlatform("windows5.1.2600")]
    public unsafe void OpenInExplorer()
    {
        if (IsEmpty)
            throw new InvalidOperationException("Path is empty");

        var itemList = PInvoke.ILCreateFromPath(Value);
        if (itemList != null)
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
