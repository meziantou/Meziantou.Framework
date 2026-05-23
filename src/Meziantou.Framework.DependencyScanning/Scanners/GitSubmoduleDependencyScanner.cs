using System.Buffers.Binary;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Git .gitmodules files for submodule references.</summary>
public sealed class GitSubmoduleDependencyScanner : DependencyScanner
{
    private const uint GitLinkMode = 0xE000;
    private const ushort ExtendedFlagsMask = 0x4000;
    private const string GitDirectoryPrefix = "gitdir:";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GitReference];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName(".gitmodules", ignoreCase: false);
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var repositoryDirectory = Path.GetDirectoryName(context.FullPath);
        if (string.IsNullOrEmpty(repositoryDirectory))
        {
            return ValueTask.CompletedTask;
        }

        var submodules = ParseGitModules(context);
        if (submodules.Count is 0)
        {
            return ValueTask.CompletedTask;
        }

        if (!TryGetGitDirectory(repositoryDirectory, out var gitDirectory))
        {
            return ValueTask.CompletedTask;
        }

        if (!TryReadGitLinks(gitDirectory, out var gitLinks))
        {
            return ValueTask.CompletedTask;
        }

        foreach (var submodule in submodules)
        {
            if (!gitLinks.TryGetValue(submodule.Path, out var sha))
                continue;

            context.ReportDependency(this, submodule.Url, sha, DependencyType.GitReference,
                nameLocation: new NonUpdatableLocation(context),
                versionLocation: new NonUpdatableLocation(context));
        }

        return ValueTask.CompletedTask;
    }

    private static List<SubmoduleEntry> ParseGitModules(ScanFileContext context)
    {
        var result = new List<SubmoduleEntry>();

        using var reader = new StreamReader(context.Content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        string? path = null;
        string? url = null;
        var inSubmoduleSection = false;

        while (reader.ReadLine() is { } line)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.Length is 0 || trimmedLine[0] is '#' or ';')
                continue;

            if (TryGetSection(trimmedLine, out var isSubmoduleSection))
            {
                AddCurrentEntryIfValid(result, inSubmoduleSection, path, url);
                inSubmoduleSection = isSubmoduleSection;
                path = null;
                url = null;
                continue;
            }

            if (!inSubmoduleSection || !TryParseAssignment(trimmedLine, out var key, out var value))
                continue;

            if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                path = NormalizeGitPath(Unquote(value));
            }
            else if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                url = Unquote(value);
            }
        }

        AddCurrentEntryIfValid(result, inSubmoduleSection, path, url);
        return result;
    }

    private static void AddCurrentEntryIfValid(List<SubmoduleEntry> submodules, bool inSubmoduleSection, string? path, string? url)
    {
        if (!inSubmoduleSection || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url))
            return;

        submodules.Add(new SubmoduleEntry(path, url));
    }

    private static bool TryGetSection(string line, out bool isSubmoduleSection)
    {
        isSubmoduleSection = false;
        if (!line.StartsWith('[') || !line.EndsWith(']'))
            return false;

        var section = line[1..^1].Trim();
        if (section.Length is 0)
            return true;

        var separatorIndex = section.IndexOfAny([' ', '"']);
        var sectionName = separatorIndex < 0 ? section : section[..separatorIndex];
        isSubmoduleSection = sectionName.Equals("submodule", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryParseAssignment(string line, out string key, out string value)
    {
        var equalIndex = line.IndexOf('=');
        if (equalIndex < 0)
        {
            key = "";
            value = "";
            return false;
        }

        key = line[..equalIndex].Trim();
        value = line[(equalIndex + 1)..].Trim();
        return key.Length > 0;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] is '"' && value[^1] is '"')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string NormalizeGitPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        while (normalizedPath.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        return normalizedPath.TrimEnd('/');
    }

    private static bool TryGetGitDirectory(string repositoryDirectory, out string gitDirectory)
    {
        var dotGitPath = Path.Combine(repositoryDirectory, ".git");
        if (Directory.Exists(dotGitPath))
        {
            gitDirectory = dotGitPath;
            return true;
        }

        if (!File.Exists(dotGitPath))
        {
            gitDirectory = "";
            return false;
        }

        try
        {
            var dotGitContent = File.ReadAllText(dotGitPath).Trim();
            if (!dotGitContent.StartsWith(GitDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                gitDirectory = "";
                return false;
            }

            var relativeGitDirectory = dotGitContent[GitDirectoryPrefix.Length..].Trim();
            if (string.IsNullOrEmpty(relativeGitDirectory))
            {
                gitDirectory = "";
                return false;
            }

            gitDirectory = Path.IsPathRooted(relativeGitDirectory)
                ? Path.GetFullPath(relativeGitDirectory)
                : Path.GetFullPath(Path.Combine(repositoryDirectory, relativeGitDirectory));
            return Directory.Exists(gitDirectory);
        }
        catch (IOException)
        {
            gitDirectory = "";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            gitDirectory = "";
            return false;
        }
    }

    private static bool TryReadGitLinks(string gitDirectory, out Dictionary<string, string> result)
    {
        var indexPath = Path.Combine(gitDirectory, "index");
        if (!File.Exists(indexPath))
        {
            result = [];
            return false;
        }

        try
        {
            using var stream = File.OpenRead(indexPath);

            Span<byte> header = stackalloc byte[12];
            if (!TryReadExactly(stream, header))
            {
                result = [];
                return false;
            }

            if (!header[..4].SequenceEqual("DIRC"u8))
            {
                result = [];
                return false;
            }

            var version = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
            if (version is not 2 and not 3)
            {
                result = [];
                return false;
            }

            var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
            result = new Dictionary<string, string>(StringComparer.Ordinal);

            Span<byte> entryHeader = stackalloc byte[62];
            for (uint i = 0; i < entryCount; i++)
            {
                var entryStart = stream.Position;
                if (!TryReadExactly(stream, entryHeader))
                {
                    result = [];
                    return false;
                }

                var mode = BinaryPrimitives.ReadUInt32BigEndian(entryHeader[24..28]);
                var flags = BinaryPrimitives.ReadUInt16BigEndian(entryHeader[60..62]);
                if ((flags & ExtendedFlagsMask) != 0)
                {
                    Span<byte> extendedFlags = stackalloc byte[2];
                    if (!TryReadExactly(stream, extendedFlags))
                    {
                        result = [];
                        return false;
                    }
                }

                var pathBytes = new List<byte>(64);
                while (true)
                {
                    var value = stream.ReadByte();
                    if (value < 0)
                    {
                        result = [];
                        return false;
                    }

                    if (value is 0)
                        break;

                    pathBytes.Add((byte)value);
                }

                var consumedBytes = checked((int)(stream.Position - entryStart));
                var paddingByteCount = (8 - (consumedBytes % 8)) % 8;
                if (paddingByteCount > 0)
                {
                    Span<byte> paddingBuffer = stackalloc byte[8];
                    if (!TryReadExactly(stream, paddingBuffer[..paddingByteCount]))
                    {
                        result = [];
                        return false;
                    }
                }

                if (mode != GitLinkMode)
                    continue;

                var path = NormalizeGitPath(System.Text.Encoding.UTF8.GetString(pathBytes.ToArray()));
                var sha = Convert.ToHexString(entryHeader[40..60]).ToLowerInvariant();
                result[path] = sha;
            }

            return true;
        }
        catch (IOException)
        {
            result = [];
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            result = [];
            return false;
        }
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read <= 0)
                return false;

            totalRead += read;
        }

        return true;
    }

    private readonly record struct SubmoduleEntry(string Path, string Url);
}
