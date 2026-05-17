using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework;

namespace Meziantou.Framework.DiffEngine;

public static class DiffTools
{
    private static readonly LaunchArguments StandardArguments = new(StandardLeftArguments, StandardRightArguments);
    private static readonly LaunchArguments RiderArguments = new(RiderLeftArguments, RiderRightArguments);
    private static readonly LaunchArguments VimArguments = new(VimLeftArguments, VimRightArguments);
    private static readonly LaunchArguments VisualStudioCodeArguments = new(VisualStudioCodeLeftArguments, VisualStudioCodeRightArguments);
    private static readonly LaunchArguments VisualStudioArguments = new(VisualStudioLeftArguments, VisualStudioRightArguments);
    private static readonly LaunchArguments WinMergeArguments = new(WinMergeLeftArguments, WinMergeRightArguments);

    private static readonly ToolDefinition[] Definitions =
    [
        new(
            DiffTool.MsWordDiff,
            SupportsText: false,
            BinaryExtensions: [".doc", ".docx"],
            LaunchArguments: StandardArguments,
            Windows: Windows(["diffword.exe"], @"%USERPROFILE%\.dotnet\tools\")),
        new(
            DiffTool.MsExcelDiff,
            SupportsText: false,
            BinaryExtensions: [".xls", ".xlsx"],
            LaunchArguments: StandardArguments,
            Windows: Windows(["diffexcel.exe"], @"%USERPROFILE%\.dotnet\tools\")),
        new(
            DiffTool.BeyondCompare,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["BComp.exe", "BCompare.exe"], @"%ProgramFiles%\Beyond Compare *\", @"%ProgramFiles(x86)%\Beyond Compare *\"),
            Linux: Linux(["bcompare"]),
            Osx: Osx(["bcompare"], "/Applications/Beyond Compare.app/Contents/MacOS/")),
        new(
            DiffTool.P4Merge,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["p4merge.exe"], @"%ProgramFiles%\Perforce\", @"%ProgramFiles(x86)%\Perforce\"),
            Linux: Linux(["p4merge"]),
            Osx: Osx(["p4merge"])),
        new(
            DiffTool.Kaleidoscope,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Osx: Osx(["ksdiff"], "/Applications/Kaleidoscope.app/Contents/MacOS/")),
        new(
            DiffTool.DeltaWalker,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["DeltaWalker.exe"], @"%ProgramFiles%\DeltaWalker\"),
            Osx: Osx(["DeltaWalker"], "/Applications/DeltaWalker.app/Contents/MacOS/")),
        new(
            DiffTool.WinMerge,
            SupportsText: true,
            BinaryExtensions:
            [
                ".bin",
                ".svg",
                ".bmp",
                ".gif",
                ".ico",
                ".jpg",
                ".jpeg",
                ".png",
                ".tif",
                ".tiff",
                ".webp",
            ],
            LaunchArguments: WinMergeArguments,
            Windows: Windows(["WinMergeU.exe"], @"%ProgramFiles%\WinMerge\", @"%LocalAppData%\Programs\WinMerge\")),
        new(
            DiffTool.TortoiseMerge,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["TortoiseMerge.exe"], @"%ProgramFiles%\TortoiseSVN\bin\", @"%ProgramFiles%\TortoiseGit\bin\")),
        new(
            DiffTool.TortoiseGitMerge,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["TortoiseGitMerge.exe"], @"%ProgramFiles%\TortoiseGit\bin\")),
        new(
            DiffTool.TortoiseGitIDiff,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["TortoiseGitIDiff.exe"], @"%ProgramFiles%\TortoiseGit\bin\")),
        new(
            DiffTool.TortoiseIDiff,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["TortoiseIDiff.exe"], @"%ProgramFiles%\TortoiseSVN\bin\")),
        new(
            DiffTool.KDiff3,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["kdiff3.exe", "KDiff3.exe"], @"%ProgramFiles%\KDiff3\"),
            Linux: Linux(["kdiff3"]),
            Osx: Osx(["kdiff3"])),
        new(
            DiffTool.TkDiff,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Osx: Osx(["tkdiff"])),
        new(
            DiffTool.Guiffy,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["guiffy.exe"], @"%ProgramFiles%\Guiffy\"),
            Osx: Osx(["guiffy"])),
        new(
            DiffTool.ExamDiff,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["ExamDiff.exe"], @"%ProgramFiles%\ExamDiff Pro\")),
        new(
            DiffTool.Diffinity,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["Diffinity.exe"], @"%ProgramFiles%\Diffinity\")),
        new(
            DiffTool.Rider,
            SupportsText: true,
            BinaryExtensions: [".svg"],
            LaunchArguments: RiderArguments,
            Windows: Windows(["rider64.exe", "rider.cmd"], @"%LOCALAPPDATA%\Programs\Rider*\bin\", @"%ProgramFiles%\JetBrains\JetBrains Rider *\bin\"),
            Linux: Linux(["rider.sh", "rider"], "%HOME%/.local/share/JetBrains/Toolbox/apps/rider/bin/"),
            Osx: Osx(["rider"], "/Applications/Rider.app/Contents/MacOS/", "/usr/local/bin/")),
        new(
            DiffTool.Vim,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: VimArguments,
            Windows: Windows(["vim.exe"], @"%ProgramFiles%\Vim\vim*\"),
            Linux: Linux(["vim"]),
            Osx: Osx(["vim"])),
        new(
            DiffTool.Neovim,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: VimArguments,
            Windows: Windows(["nvim.exe"], @"%ProgramFiles%\Neovim\bin\"),
            Linux: Linux(["nvim"]),
            Osx: Osx(["nvim"])),
        new(
            DiffTool.AraxisMerge,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["Compare.exe"], @"%ProgramFiles%\Araxis\Araxis Merge\"),
            Osx: Osx(["arxdiff"], "/Applications/Araxis Merge.app/Contents/Utilities/")),
        new(
            DiffTool.Meld,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["Meld.exe"], @"%LocalAppData%\Programs\Meld\"),
            Linux: Linux(["meld"]),
            Osx: Osx(["meld"])),
        new(
            DiffTool.SublimeMerge,
            SupportsText: true,
            BinaryExtensions: [],
            LaunchArguments: StandardArguments,
            Windows: Windows(["smerge.exe", "sublime_merge.exe"], @"%ProgramFiles%\Sublime Merge\"),
            Linux: Linux(["smerge"]),
            Osx: Osx(["smerge"], "/Applications/Sublime Merge.app/Contents/SharedSupport/bin/")),
        new(
            DiffTool.VisualStudioCode,
            SupportsText: true,
            BinaryExtensions: [".svg", ".bin"],
            LaunchArguments: VisualStudioCodeArguments,
            Windows: Windows(["code.cmd"], @"%LocalAppData%\Programs\Microsoft VS Code\bin\", @"%ProgramFiles%\Microsoft VS Code\bin\"),
            Linux: Linux(["code"]),
            Osx: Osx(["code"], "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/")),
        new(
            DiffTool.VisualStudio,
            SupportsText: true,
            BinaryExtensions: [".svg"],
            LaunchArguments: VisualStudioArguments,
            Windows: Windows(
                ["devenv.exe"],
                @"%ProgramFiles%\Microsoft Visual Studio\*\Preview\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\*\Community\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\*\Professional\Common7\IDE\",
                @"%ProgramFiles%\Microsoft Visual Studio\*\Enterprise\Common7\IDE\")),
        new(
            DiffTool.Cursor,
            SupportsText: true,
            BinaryExtensions: [".svg", ".bin"],
            LaunchArguments: VisualStudioCodeArguments,
            Windows: Windows(["Cursor.exe"], @"%ProgramFiles%\Cursor\"),
            Linux: Linux(["cursor"]),
            Osx: Osx(["cursor"], "/Applications/Cursor.app/Contents/MacOS")),
    ];

    public static bool TryFindByName(DiffTool tool, [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        foreach (var definition in Definitions)
        {
            if (definition.Tool != tool)
                continue;

            return TryResolve(definition, out resolvedTool);
        }

        resolvedTool = null;
        return false;
    }

    public static bool TryFindByExtension(string extension, [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        var tools = ResolveAvailableTools();
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length > 0)
        {
            foreach (var tool in tools)
            {
                if (tool.BinaryExtensions.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase))
                {
                    resolvedTool = tool;
                    return true;
                }
            }
        }

        resolvedTool = tools.FirstOrDefault(static t => t.SupportsText);
        return resolvedTool is not null;
    }

    private static List<ResolvedTool> ResolveAvailableTools()
    {
        var result = new List<ResolvedTool>();
        foreach (var definition in Definitions)
        {
            if (TryResolve(definition, out var resolvedTool))
            {
                result.Add(resolvedTool);
            }
        }

        return result;
    }

    private static bool TryResolve(ToolDefinition definition, [NotNullWhen(true)] out ResolvedTool? resolvedTool)
    {
        var platformSettings = GetPlatformSettings(definition);
        if (platformSettings is null)
        {
            resolvedTool = null;
            return false;
        }

        if (!TryResolveFromEnvironmentVariable(definition.Tool, platformSettings.ExecutableNames, out var exePath) &&
            !TryResolveFromDirectories(platformSettings.SearchDirectories, platformSettings.ExecutableNames, out exePath) &&
            !TryResolveFromPath(platformSettings.ExecutableNames, out exePath))
        {
            resolvedTool = null;
            return false;
        }

        resolvedTool = new ResolvedTool(definition.Tool, exePath, definition.LaunchArguments, definition.SupportsText, definition.BinaryExtensions);
        return true;
    }

    private static PlatformSettings? GetPlatformSettings(ToolDefinition definition)
    {
        if (OperatingSystem.IsWindows())
            return definition.Windows;
        if (OperatingSystem.IsLinux())
            return definition.Linux;
        if (OperatingSystem.IsMacOS())
            return definition.Osx;

        return null;
    }

    private static bool TryResolveFromEnvironmentVariable(DiffTool tool, IReadOnlyList<string> executableNames, [NotNullWhen(true)] out string? exePath)
    {
        var environmentVariable = $"DiffEngine_{tool}";
        var basePath = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            exePath = null;
            return false;
        }

        var trimmedPath = Environment.ExpandEnvironmentVariables(basePath.Trim().Trim('"'));
        if (TryResolveFile(trimmedPath, out exePath))
            return true;

        if (TryResolveFromDirectories([trimmedPath], executableNames, out exePath))
            return true;

        throw new InvalidOperationException($"Could not find exe defined by {environmentVariable}. Path: {basePath}");
    }

    private static bool TryResolveFromPath(IReadOnlyList<string> executableNames, [NotNullWhen(true)] out string? exePath)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            exePath = null;
            return false;
        }

        var pathDirectories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return TryResolveFromDirectories(pathDirectories, executableNames, out exePath);
    }

    private static bool TryResolveFromDirectories(IEnumerable<string> directories, IReadOnlyList<string> executableNames, [NotNullWhen(true)] out string? exePath)
    {
        foreach (var directoryPattern in directories)
        {
            foreach (var directory in EnumerateDirectories(directoryPattern))
            {
                foreach (var executableName in GetExecutableCandidates(executableNames))
                {
                    var candidate = Path.Combine(directory, executableName);
                    if (TryResolveFile(candidate, out exePath))
                    {
                        return true;
                    }
                }
            }
        }

        exePath = null;
        return false;
    }

    private static bool TryResolveFile(string path, [NotNullWhen(true)] out string? exePath)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        if (expandedPath.Contains('*', StringComparison.Ordinal))
        {
            if (TryResolveWildcard(expandedPath, out exePath))
                return true;
        }
        else if (File.Exists(expandedPath))
        {
            exePath = FullPath.FromPath(expandedPath);
            return true;
        }

        exePath = null;
        return false;
    }

    private static bool TryResolveWildcard(string path, [NotNullWhen(true)] out string? exePath)
    {
        var filePart = Path.GetFileName(path);
        var directoryPart = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(filePart) || string.IsNullOrEmpty(directoryPart))
        {
            exePath = null;
            return false;
        }

        foreach (var directory in EnumerateDirectories(directoryPart))
        {
            var candidate = Path.Combine(directory, filePart);
            if (!candidate.Contains('*', StringComparison.Ordinal) && File.Exists(candidate))
            {
                exePath = FullPath.FromPath(candidate);
                return true;
            }
        }

        exePath = null;
        return false;
    }

    private static IEnumerable<string> EnumerateDirectories(string directoryPattern)
    {
        if (string.IsNullOrWhiteSpace(directoryPattern))
            yield break;

        var expandedPattern = Environment.ExpandEnvironmentVariables(directoryPattern.Trim().Trim('"'));
        if (!expandedPattern.Contains('*', StringComparison.Ordinal))
        {
            if (Directory.Exists(expandedPattern))
            {
                yield return expandedPattern;
            }

            yield break;
        }

        var segments = expandedPattern.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (segments.Length == 0)
            yield break;

        var currentRoots = new List<string>
        {
            segments[0] + Path.DirectorySeparatorChar,
        };

        foreach (var segment in segments.Skip(1))
        {
            if (segment.Length == 0)
                continue;

            var nextRoots = new List<string>();
            foreach (var root in currentRoots)
            {
                if (segment.Contains('*', StringComparison.Ordinal))
                {
                    try
                    {
                        nextRoots.AddRange(
                            Directory
                                .EnumerateDirectories(root, segment)
                                .OrderByDescending(Directory.GetLastWriteTimeUtc));
                    }
                    catch (DirectoryNotFoundException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
                else
                {
                    var candidate = Path.Combine(root, segment);
                    if (Directory.Exists(candidate))
                    {
                        nextRoots.Add(candidate);
                    }
                }
            }

            if (nextRoots.Count == 0)
                yield break;

            currentRoots = nextRoots;
        }

        foreach (var root in currentRoots)
        {
            yield return root;
        }
    }

    private static HashSet<string> GetExecutableCandidates(IReadOnlyList<string> executableNames)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var executableName in executableNames)
        {
            result.Add(executableName);
            if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(executableName)))
            {
                foreach (var extension in GetWindowsExecutableExtensions())
                {
                    result.Add(executableName + extension);
                }
            }
        }

        return result;
    }

    private static string[] GetWindowsExecutableExtensions()
    {
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExtensions))
        {
            return [".exe", ".cmd", ".bat", ".com"];
        }

        return pathExtensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "";

        var trimmed = extension.Trim();
        return trimmed[0] == '.' ? trimmed : "." + trimmed;
    }

    private static string StandardLeftArguments(string temp, string target) => $"\"{target}\" \"{temp}\"";
    private static string StandardRightArguments(string temp, string target) => $"\"{temp}\" \"{target}\"";

    private static string RiderLeftArguments(string temp, string target) => $"diff \"{target}\" \"{temp}\"";
    private static string RiderRightArguments(string temp, string target) => $"diff \"{temp}\" \"{target}\"";

    private static string VimLeftArguments(string temp, string target) => $"-d \"{target}\" \"{temp}\"";
    private static string VimRightArguments(string temp, string target) => $"-d \"{temp}\" \"{target}\"";

    private static string VisualStudioCodeLeftArguments(string temp, string target) => $"--diff \"{target}\" \"{temp}\"";
    private static string VisualStudioCodeRightArguments(string temp, string target) => $"--diff \"{temp}\" \"{target}\"";

    private static string VisualStudioLeftArguments(string temp, string target)
    {
        var tempTitle = Path.GetFileName(temp);
        var targetTitle = Path.GetFileName(target);
        return $"/diff \"{target}\" \"{temp}\" \"{targetTitle}\" \"{tempTitle}\"";
    }

    private static string VisualStudioRightArguments(string temp, string target)
    {
        var tempTitle = Path.GetFileName(temp);
        var targetTitle = Path.GetFileName(target);
        return $"/diff \"{temp}\" \"{target}\" \"{tempTitle}\" \"{targetTitle}\"";
    }

    private static string WinMergeLeftArguments(string temp, string target)
    {
        var tempTitle = Path.GetFileName(temp);
        var targetTitle = Path.GetFileName(target);
        return $"/u /wr /e \"{target}\" \"{temp}\" /dl \"{targetTitle}\" /dr \"{tempTitle}\" /cfg Backup/EnableFile=0";
    }

    private static string WinMergeRightArguments(string temp, string target)
    {
        var tempTitle = Path.GetFileName(temp);
        var targetTitle = Path.GetFileName(target);
        return $"/u /wl /e \"{temp}\" \"{target}\" /dl \"{tempTitle}\" /dr \"{targetTitle}\" /cfg Backup/EnableFile=0";
    }

    private static PlatformSettings Windows(string[] executableNames, params string[] searchDirectories) => new(executableNames, searchDirectories);
    private static PlatformSettings Linux(string[] executableNames, params string[] searchDirectories) => new(executableNames, searchDirectories);
    private static PlatformSettings Osx(string[] executableNames, params string[] searchDirectories) => new(executableNames, searchDirectories);

    private sealed record ToolDefinition(
        DiffTool Tool,
        bool SupportsText,
        string[] BinaryExtensions,
        LaunchArguments LaunchArguments,
        PlatformSettings? Windows = null,
        PlatformSettings? Linux = null,
        PlatformSettings? Osx = null);

    private sealed record PlatformSettings(string[] ExecutableNames, string[] SearchDirectories);
}
