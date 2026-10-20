using System.Xml;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads the font directories of the configuration of fontconfig: the <c>dir</c> elements of <c>fonts.conf</c> and of
/// the files it includes (https://www.freedesktop.org/software/fontconfig/fontconfig-user.html).
/// </summary>
/// <remarks>
/// Only the elements that declare directories are read (<c>dir</c>, <c>include</c>, <c>reset-dirs</c>); the rules of
/// fontconfig (<c>match</c>, <c>selectfont</c>, ...) do not change the files that are installed. A file that is missing,
/// that cannot be read or that is not well-formed adds the directories read before the error.
/// </remarks>
internal static class FontConfigFile
{
    private const string DefaultConfigDirectory = "/etc/fonts";
    private const string DefaultConfigFile = "fonts.conf";
    private const int MaxIncludeDepth = 16;
    private const long MaxFileLength = 16 * 1024 * 1024;

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        MaxCharactersInDocument = MaxFileLength,
    };

    /// <summary>Reads the font directories of the configuration: <c>$FONTCONFIG_FILE</c>, else <c>fonts.conf</c> in <c>$FONTCONFIG_PATH</c> or <c>/etc/fonts</c>.</summary>
    /// <param name="getEnvironmentVariable">Reads an environment variable.</param>
    /// <param name="home">The home directory of the user, or <see langword="null"/>.</param>
    /// <returns>The full paths of the directories, in the order of the configuration.</returns>
    public static List<string> ReadDirectories(Func<string, string?> getEnvironmentVariable, string? home)
    {
        var configDirectories = new List<string>();
        var configPath = getEnvironmentVariable("FONTCONFIG_PATH");
        if (!string.IsNullOrEmpty(configPath))
        {
            // A list of the platform: ':' on Unix, ';' on Windows, where a path has a drive letter
            foreach (var directory in configPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Path.IsPathRooted(directory))
                {
                    configDirectories.Add(directory);
                }
            }
        }

        configDirectories.Add(DefaultConfigDirectory);

        var file = getEnvironmentVariable("FONTCONFIG_FILE");
        if (string.IsNullOrEmpty(file))
        {
            file = DefaultConfigFile;
        }

        var reader = new ConfigReader(getEnvironmentVariable, home, configDirectories);
        if (reader.ResolveConfigFile(file, currentFile: null) is { } path)
        {
            reader.Read(path, depth: 0);
        }

        return reader.Directories;
    }

    private sealed class ConfigReader(Func<string, string?> getEnvironmentVariable, string? home, List<string> configDirectories)
    {
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

        public List<string> Directories { get; } = [];

        /// <summary>Reads a file, or the files of a directory whose name starts with a digit and ends with <c>.conf</c>, in order.</summary>
        public void Read(string path, int depth)
        {
            if (depth > MaxIncludeDepth)
                return;

            try
            {
                if (Directory.Exists(path))
                {
                    var files = new List<string>();
                    foreach (var file in Directory.EnumerateFiles(path, "*.conf"))
                    {
                        var name = Path.GetFileName(file);
                        if (name.Length != 0 && char.IsAsciiDigit(name[0]))
                        {
                            files.Add(file);
                        }
                    }

                    files.Sort(StringComparer.Ordinal);
                    foreach (var file in files)
                    {
                        ReadFile(file, depth);
                    }
                }
                else if (File.Exists(path))
                {
                    ReadFile(path, depth);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A directory that cannot be listed adds no font directory
            }
        }

        private void ReadFile(string path, int depth)
        {
            // An include that leads back to a file being read, or already read, adds nothing more.
            if (!_visited.Add(Path.GetFullPath(path)))
                return;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaxFileLength)
                    return;

                using var reader = XmlReader.Create(stream, ReaderSettings);
                reader.MoveToContent();
                while (!reader.EOF)
                {
                    // The elements are the children of the root element, <fontconfig>.
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                    {
                        switch (reader.LocalName)
                        {
                            case "dir":
                                {
                                    var prefix = reader.GetAttribute("prefix");
                                    var value = reader.ReadElementContentAsString();
                                    if (ResolveDirectory(value, prefix, path) is { } directory)
                                    {
                                        Directories.Add(directory);
                                    }

                                    continue;
                                }

                            case "include":
                                {
                                    var prefix = reader.GetAttribute("prefix");
                                    var value = reader.ReadElementContentAsString();
                                    if (ResolveInclude(value, prefix, path) is { } include)
                                    {
                                        Read(include, depth + 1);
                                    }

                                    continue;
                                }

                            case "reset-dirs":
                                Directories.Clear();
                                break;
                        }
                    }

                    reader.Read();
                }
            }
            catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The directories read before the error are kept
            }
        }

        /// <summary>
        /// Resolves the value of a <c>dir</c> element: <c>~</c> is the home directory; the prefix <c>xdg</c> is
        /// <c>$XDG_DATA_HOME</c>, <c>relative</c> the directory of the file, and <c>default</c> or <c>cwd</c> the current
        /// directory.
        /// </summary>
        private string? ResolveDirectory(string value, string? prefix, string currentFile)
        {
            value = value.Trim();
            if (value.Length == 0)
                return null;

            var path = IsHomePath(value) ? ExpandHome(value) : prefix switch
            {
                "xdg" => Combine(SystemFontDirectories.GetXdgDataHome(getEnvironmentVariable, home), value),
                "relative" => Combine(Path.GetDirectoryName(currentFile), value),
                _ => value,
            };

            return path is null ? null : Path.GetFullPath(path);
        }

        /// <summary>
        /// Resolves the value of an <c>include</c> element: <c>~</c> is the home directory, the prefix <c>xdg</c> is
        /// <c>$XDG_CONFIG_HOME</c>, and a relative path is looked for next to the file, then in the configuration directories.
        /// </summary>
        private string? ResolveInclude(string value, string? prefix, string currentFile)
        {
            value = value.Trim();
            if (value.Length == 0)
                return null;

            var path = IsHomePath(value) ? ExpandHome(value) : prefix switch
            {
                "xdg" => Combine(SystemFontDirectories.GetXdgConfigHome(getEnvironmentVariable, home), value),
                "relative" => Combine(Path.GetDirectoryName(currentFile), value),
                _ => ResolveConfigFile(value, currentFile),
            };

            return path is null ? null : Path.GetFullPath(path);
        }

        /// <summary>Resolves a file of the configuration: a full path, else the first that exists next to the current file or in a configuration directory.</summary>
        public string? ResolveConfigFile(string value, string? currentFile)
        {
            if (Path.IsPathRooted(value))
                return value;

            if (currentFile is not null && Combine(Path.GetDirectoryName(currentFile), value) is { } sibling && Exists(sibling))
                return sibling;

            foreach (var directory in configDirectories)
            {
                var candidate = Path.Combine(directory, value);
                if (Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool IsHomePath(string value) => value == "~" || value.StartsWith("~/", StringComparison.Ordinal);

        /// <summary>Expands a path from the home directory; a user without a home directory has no such path, as in fontconfig.</summary>
        private string? ExpandHome(string value) => home is null ? null : Path.Join(home, value.AsSpan(1).TrimStart('/'));

        private static string? Combine(string? directory, string value) => string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, value);

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    }
}
