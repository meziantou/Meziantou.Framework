namespace Meziantou.Framework.Globbing;

/// <summary>Specifies the format of the paths returned when enumerating the entries that match a glob pattern.</summary>
public enum GlobPathFormat
{
    /// <summary>The full path of the entry.</summary>
    FullPath,

    /// <summary>
    ///     The path of the entry relative to the enumerated directory, using <see cref="Path.DirectorySeparatorChar"/>
    ///     as the separator. This is the path the pattern matches, such as <c>src/Program.cs</c> on Linux.
    /// </summary>
    RelativePath,
}
