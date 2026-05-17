using System.Collections.ObjectModel;

namespace Meziantou.Framework.DiffEngine;

public sealed class ResolvedTool
{
    private readonly LaunchArguments _launchArguments;

    internal ResolvedTool(DiffTool tool, string exePath, LaunchArguments launchArguments, bool supportsText, IEnumerable<string> binaryExtensions)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        ArgumentNullException.ThrowIfNull(binaryExtensions);

        Tool = tool;
        Name = tool.ToString();
        ExePath = exePath;
        _launchArguments = launchArguments;
        SupportsText = supportsText;
        BinaryExtensions = new ReadOnlyCollection<string>(binaryExtensions.Select(static extension => NormalizeExtension(extension)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public string Name { get; }
    public DiffTool Tool { get; }
    public string ExePath { get; }
    public bool SupportsText { get; }
    public IReadOnlyCollection<string> BinaryExtensions { get; }

    public string GetArguments(string tempFile, string targetFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(tempFile);
        ArgumentException.ThrowIfNullOrEmpty(targetFile);

        return TargetPosition.TargetOnLeft
            ? _launchArguments.Left(tempFile, targetFile)
            : _launchArguments.Right(tempFile, targetFile);
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);
        return extension[0] == '.' ? extension : "." + extension;
    }
}
