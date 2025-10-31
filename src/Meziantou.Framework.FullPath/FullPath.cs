using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using Windows.Win32;

namespace Meziantou.Framework;

/// <summary>
/// Represents a fully qualified file system path.
/// </summary>
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

    /// <summary>
    /// Gets an empty <see cref="FullPath"/> value.
    /// </summary>
    public static FullPath Empty => default;

    /// <summary>
    /// Gets a value indicating whether this path is empty.
    /// </summary>
    [MemberNotNullWhen(returnValue: false, nameof(_value))]
    public bool IsEmpty => _value is null;

    /// <summary>
    /// Gets the string representation of the path.
    /// </summary>
    public string Value => _value ?? "";

    public static implicit operator string(FullPath fullPath) => fullPath.ToString();

    public static bool operator ==(FullPath path1, FullPath path2) => path1.Equals(path2);
    public static bool operator !=(FullPath path1, FullPath path2) => !(path1 == path2);
    public static bool operator <(FullPath path1, FullPath path2) => path1.CompareTo(path2) < 0;
    public static bool operator >(FullPath path1, FullPath path2) => path1.CompareTo(path2) > 0;
    public static bool operator <=(FullPath path1, FullPath path2) => path1.CompareTo(path2) <= 0;
    public static bool operator >=(FullPath path1, FullPath path2) => path1.CompareTo(path2) >= 0;

    /// <summary>
    /// Combines a root path with a relative path.
    /// </summary>
    public static FullPath operator /(FullPath rootPath, string relativePath) => Combine(rootPath, relativePath);

    /// <summary>
    /// Gets the parent directory of the current path.
    /// </summary>
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

    /// <summary>
    /// Gets the file or directory name.
    /// </summary>
    public string Name => Path.GetFileName(_value) ?? "";

    /// <summary>
    /// Gets the file or directory name without extension.
    /// </summary>
    public string NameWithoutExtension => Path.GetFileNameWithoutExtension(_value) ?? "";

    /// <summary>
    /// Gets the file extension including the period.
    /// </summary>
    public string Extension => Path.GetExtension(_value) ?? "";

    public int CompareTo(FullPath other) => FullPathComparer.Default.Compare(this, other);

    /// <summary>
    /// Compares this path to another path.
    /// </summary>
    /// <param name="other">The path to compare to.</param>
    /// <param name="ignoreCase">Whether to ignore case when comparing paths.</param>
    public int CompareTo(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Compare(this, other);

    public override bool Equals(object? obj) => obj is FullPath path && Equals(path);
    public bool Equals(FullPath other) => FullPathComparer.Default.Equals(this, other);

    /// <summary>
    /// Determines whether this path is equal to another path.
    /// </summary>
    /// <param name="other">The path to compare to.</param>
    /// <param name="ignoreCase">Whether to ignore case when comparing paths.</param>
    public bool Equals(FullPath other, bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).Equals(this, other);

    public override int GetHashCode() => FullPathComparer.Default.GetHashCode(this);

    /// <summary>
    /// Gets the hash code for this path.
    /// </summary>
    /// <param name="ignoreCase">Whether to ignore case when computing the hash code.</param>
    public int GetHashCode(bool ignoreCase) => FullPathComparer.GetComparer(ignoreCase).GetHashCode(this);

    public override string ToString() => Value;

    /// <summary>
    /// Makes this path relative to the specified root path.
    /// </summary>
    /// <param name="rootPath">The root path.</param>
    /// <returns>A relative path from <paramref name="rootPath"/> to this path.</returns>
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

    public FullPath ChangeExtension(string? extension)
    {
        if (IsEmpty)
            return Empty;

        return new FullPath(Path.ChangeExtension(Value, extension));
    }

    /// <summary>
    /// Gets the path to the system temporary directory.
    /// </summary>
    public static FullPath GetTempPath() => FromPath(Path.GetTempPath());

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static FullPath GetTempFileName() => FromPath(Path.GetTempFileName());

    /// <summary>
    /// Creates a temporary file and returns its path.
    /// </summary>
    public static FullPath CreateTempFile() => FromPath(Path.GetTempFileName());

    /// <summary>
    /// Gets the path to a special folder.
    /// </summary>
    /// <param name="folder">The special folder to get the path for.</param>
    public static FullPath GetFolderPath(Environment.SpecialFolder folder) => FromPath(Environment.GetFolderPath(folder));

    /// <summary>
    /// Gets the path to a known folder on Windows.
    /// </summary>
    /// <param name="knownFolder">The known folder to get the path for.</param>
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

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    public static FullPath CurrentDirectory() => FromPath(Environment.CurrentDirectory);

    /// <summary>
    /// Creates a <see cref="FullPath"/> from a string path by resolving it to an absolute path.
    /// </summary>
    /// <param name="path">The path to convert.</param>
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

    /// <summary>
    /// Combines two path components.
    /// </summary>
    public static FullPath Combine(string rootPath, string relativePath) => FromPath(Path.Combine(rootPath, relativePath));

    /// <summary>
    /// Combines three path components.
    /// </summary>
    public static FullPath Combine(string rootPath, string path1, string path2) => FromPath(Path.Combine(rootPath, path1, path2));

    /// <summary>
    /// Combines four path components.
    /// </summary>
    public static FullPath Combine(string rootPath, string path1, string path2, string path3) => FromPath(Path.Combine(rootPath, path1, path2, path3));

    /// <summary>
    /// Combines multiple path components.
    /// </summary>
    public static FullPath Combine(params string[] paths) => FromPath(Path.Combine(paths));

    /// <summary>
    /// Combines a full path with a relative path.
    /// </summary>
    public static FullPath Combine(FullPath rootPath, string relativePath)
    {
        if (rootPath.IsEmpty)
            return FromPath(relativePath);

        return FromPath(Path.Combine(rootPath._value, relativePath));
    }

    public static FullPath Combine(FullPath rootPath, string path1, string path2)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(path1, path2));

        return FromPath(Path.Combine(rootPath._value, path1, path2));
    }

    public static FullPath Combine(FullPath rootPath, params string[] paths)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(paths));

        return FromPath(Path.Combine(rootPath._value, Path.Combine(paths)));
    }

#if NET9_0_OR_GREATER
    public static FullPath Combine(FullPath rootPath, params ReadOnlySpan<string> paths)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(paths));

        return FromPath(Path.Combine([rootPath._value, .. paths]));
    }
#endif

    public static FullPath Combine(FullPath rootPath, string path1, string path2, string path3)
    {
        if (rootPath.IsEmpty)
            return FromPath(Path.Combine(path1, path2, path3));

        return FromPath(Path.Combine(rootPath._value, path1, path2, path3));
    }

    public static FullPath FromFileSystemInfo(FileSystemInfo? fsi)
    {
        if (fsi is null)
            return Empty;

        return FromPath(fsi.FullName);
    }

    public bool IsSymbolicLink()
    {
        if (IsEmpty)
            return false;

        return Symlink.IsSymbolicLink(_value);
    }

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

    public bool TryFindFirstAncestor(Func<FullPath, bool> predicate, out FullPath result)
    {
        return Parent.TryFindFirstAncestorOrSelf(predicate, out result);
    }

    public bool TryGetSymbolicLinkTarget([NotNullWhen(true)] out FullPath? result)
    {
        return TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out result);
    }

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
}
