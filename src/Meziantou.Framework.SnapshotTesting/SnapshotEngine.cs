using System.Globalization;
using System.Text;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting;

internal static class SnapshotEngine
{
    public static void Validate(object? value, SnapshotSettings settings, string? filePath, int lineNumber, string? memberName, SnapshotTestContext? testContext)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var callerContext = SnapshotCallerContext.Create(filePath, lineNumber, memberName);
        var snapshotType = SnapshotType.Default;
        var serializer = settings.GetSnapshotSerializer(snapshotType);
        var serialized = serializer.Serialize(snapshotType, value);

        if (serialized is null || serialized.Count == 0)
            throw new SnapshotException("Serializer returned no snapshot data.");

        var actualFiles = BuildActualFiles(settings, callerContext, snapshotType, serialized, testContext);
        var expectedFilePaths = DiscoverExpectedFilePaths(actualFiles);
        var expectedFiles = LoadSnapshotFiles(expectedFilePaths);

        var comparison = Compare(settings, snapshotType, actualFiles, expectedFiles);
        if (!comparison.HasDifferences && !settings.ForceUpdateSnapshots)
            return;

        if (!settings.SnapshotUpdateStrategy.CanUpdateSnapshotInternal(settings, callerContext.SourceFilePath, comparison.ExpectedSummary, comparison.ActualSummary))
        {
            if (comparison.HasDifferences)
            {
                ThrowAssertion(settings, comparison.Message);
            }

            return;
        }

        WriteSnapshots(settings, actualFiles, expectedFiles.Keys);

        if (comparison.HasDifferences && settings.SnapshotUpdateStrategy.MustReportError(settings, callerContext.SourceFilePath))
        {
            ThrowAssertion(settings, comparison.Message);
        }
    }

    private static SnapshotComparisonResult Compare(SnapshotSettings settings, SnapshotType type, List<SnapshotFile> actualFiles, Dictionary<FullPath, SnapshotData> expectedFiles)
    {
        var actualByPath = actualFiles.ToDictionary(item => item.FilePath, item => item.Data);
        var expectedPaths = new HashSet<FullPath>(expectedFiles.Keys);
        var actualPaths = new HashSet<FullPath>(actualByPath.Keys);

        var missingPaths = actualPaths.Where(path => !expectedPaths.Contains(path)).ToArray();
        var extraPaths = expectedPaths.Where(path => !actualPaths.Contains(path)).ToArray();

        var comparer = settings.GetSnapshotComparer(type);
        var changedPaths = new List<FullPath>();
        foreach (var path in expectedPaths.Intersect(actualPaths))
        {
            if (!comparer.Equals(expectedFiles[path], actualByPath[path]))
            {
                changedPaths.Add(path);
            }
        }

        if (missingPaths.Length == 0 && extraPaths.Length == 0 && changedPaths.Count == 0)
        {
            return SnapshotComparisonResult.NoDifference;
        }

        var message = BuildMessage(settings, missingPaths, extraPaths, changedPaths, expectedFiles, actualByPath);
        return new SnapshotComparisonResult(true, message, FormatSummary(expectedPaths), FormatSummary(actualPaths));
    }

    private static string BuildMessage(
        SnapshotSettings settings,
        FullPath[] missingPaths,
        FullPath[] extraPaths,
        List<FullPath> changedPaths,
        Dictionary<FullPath, SnapshotData> expectedFiles,
        Dictionary<FullPath, SnapshotData> actualFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Snapshots do not match:");

        if (missingPaths.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Missing snapshot files:");
            foreach (var path in missingPaths.OrderBy(static p => p.Value, StringComparer.Ordinal))
            {
                sb.Append("  + ").AppendLine(path.Value);
            }
        }

        if (extraPaths.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unexpected snapshot files:");
            foreach (var path in extraPaths.OrderBy(static p => p.Value, StringComparer.Ordinal))
            {
                sb.Append("  - ").AppendLine(path.Value);
            }
        }

        if (changedPaths.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changed snapshot files:");
            foreach (var path in changedPaths.OrderBy(static p => p.Value, StringComparer.Ordinal))
            {
                sb.Append("  * ").AppendLine(path.Value);
                if (TryDecodeAsText(expectedFiles[path].Data, out var expectedText) && TryDecodeAsText(actualFiles[path].Data, out var actualText))
                {
                    sb.AppendLine();
                    sb.AppendLine(settings.ErrorMessageFormatter.FormatMessage(expectedText, actualText));
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string? FormatSummary(IEnumerable<FullPath> paths)
    {
        var items = paths.Select(static p => p.Value).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        if (items.Length == 0)
            return null;

        return string.Join('\n', items);
    }

    private static Dictionary<FullPath, SnapshotData> LoadSnapshotFiles(IEnumerable<FullPath> expectedPaths)
    {
        var result = new Dictionary<FullPath, SnapshotData>();
        foreach (var path in expectedPaths)
        {
            if (!File.Exists(path))
                continue;

            var extension = path.Extension;
            if (extension.Length > 0 && extension[0] == '.')
            {
                extension = extension[1..];
            }

            result[path] = new SnapshotData(extension, File.ReadAllBytes(path));
        }

        return result;
    }

    private static List<SnapshotFile> BuildActualFiles(
        SnapshotSettings settings,
        SnapshotCallerContext callerContext,
        SnapshotType type,
        IReadOnlyList<SnapshotData> serialized,
        SnapshotTestContext? testContext)
    {
        var result = new List<SnapshotFile>(serialized.Count);
        for (var index = 0; index < serialized.Count; index++)
        {
            var snapshotData = serialized[index];
            var fileName = settings.FileNameStrategy(new SnapshotFileNameContext(
                callerContext.SourceFilePath,
                callerContext.MethodName,
                callerContext.MemberName,
                callerContext.LineNumber,
                type,
                index,
                snapshotData.Extension,
                testContext,
                settings));

            var path = settings.PathStrategy(new SnapshotPathContext(
                callerContext.SourceFilePath,
                callerContext.MethodName,
                callerContext.MemberName,
                callerContext.LineNumber,
                type,
                index,
                fileName,
                testContext,
                settings));

            result.Add(new SnapshotFile(path, snapshotData));
        }

        return result;
    }

    private static IReadOnlyCollection<FullPath> DiscoverExpectedFilePaths(List<SnapshotFile> actualFiles)
    {
        var actualPaths = new HashSet<FullPath>(actualFiles.Select(f => f.FilePath));
        if (actualFiles.Count == 0)
            return actualPaths;

        var firstFile = actualFiles[0].FilePath;
        if (!Directory.Exists(firstFile.Parent))
            return actualPaths.Where(path => File.Exists(path.Value)).ToArray();

        var firstName = firstFile.NameWithoutExtension;
        var separatorIndex = firstName.LastIndexOf('_');
        if (separatorIndex < 0)
            return actualPaths.Where(path => File.Exists(path.Value)).ToArray();

        var prefix = firstName[..(separatorIndex + 1)];
        var expected = new HashSet<FullPath>();
        foreach (var path in Directory.EnumerateFiles(firstFile.Parent))
        {
            var candidate = FullPath.FromPath(path);
            var name = candidate.NameWithoutExtension;
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var suffix = name[prefix.Length..];
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                continue;

            expected.Add(candidate);
        }

        foreach (var path in actualPaths)
        {
            expected.Add(path);
        }

        return expected;
    }

    private static void WriteSnapshots(SnapshotSettings settings, IReadOnlyList<SnapshotFile> actualFiles, IEnumerable<FullPath> existingPaths)
    {
        var actualPaths = new HashSet<FullPath>(actualFiles.Select(static item => item.FilePath));

        foreach (var snapshotFile in actualFiles)
        {
            Directory.CreateDirectory(snapshotFile.FilePath.Parent);

            var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
            var tempFilePath = Path.Combine(tempFolder, snapshotFile.FilePath.Name);
            File.WriteAllBytes(tempFilePath, snapshotFile.Data.Data);

            if (!File.Exists(snapshotFile.FilePath))
            {
                using (File.Create(snapshotFile.FilePath))
                {
                }
            }

            settings.SnapshotUpdateStrategy.UpdateFile(settings, snapshotFile.FilePath, tempFilePath);
        }

        foreach (var existingPath in existingPaths)
        {
            if (actualPaths.Contains(existingPath))
                continue;

            if (File.Exists(existingPath))
            {
                var fileInfo = new FileInfo(existingPath);
                fileInfo.TrySetReadOnly(false);
                fileInfo.Delete();
            }
        }
    }

    private static void ThrowAssertion(SnapshotSettings settings, string message)
    {
        throw settings.AssertionExceptionCreator.CreateException(message);
    }

    private static bool TryDecodeAsText(byte[] data, out string value)
    {
        try
        {
            value = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            value = "";
            return false;
        }
    }

    private readonly record struct SnapshotComparisonResult(bool HasDifferences, string Message, string? ExpectedSummary, string? ActualSummary)
    {
        public static SnapshotComparisonResult NoDifference { get; } = new(false, "", null, null);
    }
}
