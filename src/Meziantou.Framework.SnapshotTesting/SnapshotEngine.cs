using System.Collections.Concurrent;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting;

internal static class SnapshotEngine
{
    private static readonly UTF8Encoding Utf8WithoutBomExceptionFallback = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly ConcurrentDictionary<FullPath, VerifiedSnapshotFileCache> VerifiedSnapshotFiles = new();

    public static void Validate(SnapshotType? type, object? value, SnapshotSettings settings, string? filePath, int lineNumber, string? memberName, SnapshotTestContext? testContext)
    {
        ArgumentNullException.ThrowIfNull(settings);

        type ??= SnapshotType.Default;
        var callerContext = SnapshotCallerContext.Create(filePath, lineNumber, memberName);
        var serialized = Serialize(settings, type, value);

        if (serialized is null || serialized.Count == 0)
            throw new SnapshotException("Serializer returned no snapshot data.");

        testContext ??= SnapshotTestContext.Get();

        var actualFiles = BuildActualFiles(settings, callerContext, type, serialized, testContext);
        var expectedFilePaths = DiscoverExpectedFilePaths(actualFiles);
        var expectedFiles = LoadSnapshotFiles(expectedFilePaths);

        var comparison = Compare(settings, type, actualFiles, expectedFiles);
        var filesToUpdate = BuildSnapshotFilesToUpdate(actualFiles, comparison.PathsToUpdate);
        WriteActualSnapshots(filesToUpdate);

        if (!comparison.HasDifferences && !settings.ForceUpdateSnapshots)
            return;

        if (!settings.SnapshotUpdateStrategy.CanUpdateSnapshotInternal(settings, callerContext.SourceFilePath, comparison.ExpectedSummary, comparison.ActualSummary))
        {
            if (comparison.HasDifferences)
            {
                ThrowAssertion(comparison.Message);
            }

            return;
        }

        if (settings.ForceUpdateSnapshots)
        {
            // WriteActualSnapshots only wrote the files that differ. A forced update consumes every actual
            // file, so the ones whose snapshot already matched must be written too: otherwise the update
            // either fails on a missing file or promotes whatever an earlier failed run left there.
            var writtenPaths = new HashSet<FullPath>(filesToUpdate.Select(item => item.VerifiedPath));
            var remainingFiles = BuildSnapshotFilesToUpdate(actualFiles, [.. actualFiles.Select(item => item.FilePath).Where(path => !writtenPaths.Contains(path))]);
            WriteActualSnapshots(remainingFiles);
            filesToUpdate.AddRange(remainingFiles);
        }

        ApplySnapshotUpdates(settings, filesToUpdate, comparison.ExtraPaths);

        if (comparison.HasDifferences && settings.SnapshotUpdateStrategy.MustReportError(settings, callerContext.SourceFilePath))
        {
            ThrowAssertion(comparison.Message);
        }
    }

    private static IReadOnlyList<SnapshotData> Serialize(SnapshotSettings settings, SnapshotType type, object? value)
    {
        var data = settings.Serializers.Serialize(type, value).Data;
        if (settings.Scrubbers.Count == 0)
            return data;

        var result = new List<SnapshotData>(data.Count);
        foreach (var snapshotData in data)
        {
            result.Add(ApplyScrubbers(snapshotData, settings.Scrubbers));
        }

        return result;
    }

    private static SnapshotData ApplyScrubbers(SnapshotData snapshotData, IList<Scrubber> scrubbers)
    {
        string text;
        try
        {
            text = Utf8WithoutBomExceptionFallback.GetString(snapshotData.Data);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SnapshotException("Snapshot scrubbers can only be applied to UTF-8 text snapshots.", ex);
        }

        foreach (var scrubber in scrubbers)
        {
            text = scrubber.Scrub(text);
        }

        return new SnapshotData(snapshotData.Extension, Encoding.UTF8.GetBytes(text));
    }

    private static SnapshotComparisonResult Compare(SnapshotSettings settings, SnapshotType type, List<SnapshotFile> actualFiles, Dictionary<FullPath, SnapshotData> expectedFiles)
    {
        // A single snapshot compared with its verified file is what every test does, and the bookkeeping
        // below - two dictionaries, three sets and a couple of LINQ passes - is only needed to describe a
        // difference between two sets of files. Both outcomes are decided here so that a mismatch does not
        // run the comparer a second time on the same pair.
        if (actualFiles.Count == 1 && expectedFiles.Count == 1)
        {
            var actualFile = actualFiles[0];
            if (expectedFiles.TryGetValue(actualFile.FilePath, out var expectedData))
            {
                if (GetComparer(settings, type, actualFile.Data).Equals(expectedData, actualFile.Data))
                    return SnapshotComparisonResult.NoDifference;

                // The two sets hold the same single path, so its content is the only thing that can differ.
                FullPath[] changedPath = [actualFile.FilePath];
                var summary = FormatSummary(changedPath);
                return new SnapshotComparisonResult(HasDifferences: true, BuildMessage([], [], changedPath), summary, summary, changedPath, [], [], changedPath);
            }
        }

        var actualByPath = actualFiles.ToDictionary(item => item.FilePath, item => item.Data);
        var expectedPaths = new HashSet<FullPath>(expectedFiles.Keys);
        var actualPaths = new HashSet<FullPath>(actualByPath.Keys);

        var missingPaths = actualPaths.Where(path => !expectedPaths.Contains(path)).ToArray();
        var extraPaths = expectedPaths.Where(path => !actualPaths.Contains(path)).ToArray();

        var changedPaths = new List<FullPath>();
        foreach (var path in expectedPaths.Intersect(actualPaths))
        {
            var actualData = actualByPath[path];
            if (!GetComparer(settings, type, actualData).Equals(expectedFiles[path], actualData))
            {
                changedPaths.Add(path);
            }
        }

        if (missingPaths.Length == 0 && extraPaths.Length == 0 && changedPaths.Count == 0)
        {
            return SnapshotComparisonResult.NoDifference;
        }

        FullPath[] changedPathArray = [.. changedPaths];
        var message = BuildMessage(missingPaths, extraPaths, changedPathArray);
        var pathsToUpdate = missingPaths.Concat(changedPaths).Distinct().ToArray();
        return new SnapshotComparisonResult(HasDifferences: true, message, FormatSummary(expectedPaths), FormatSummary(actualPaths), changedPathArray, missingPaths, extraPaths, pathsToUpdate);
    }

    /// <summary>
    /// Resolves the comparer for a snapshot file. A serializer may store a different format than the one that
    /// was requested - an animated GIF becomes PNG frames, a source generator result becomes C# files - and
    /// the comparison is about the bytes that ended up on disk, so a comparer registered for the stored format
    /// wins. The requested type stays as the fallback, so an explicit registration for it still applies when
    /// nothing is registered for the stored format.
    /// </summary>
    private static ISnapshotComparer GetComparer(SnapshotSettings settings, SnapshotType requestedType, SnapshotData data)
    {
        if (!string.IsNullOrEmpty(data.Extension))
        {
            var storedType = SnapshotType.Create(data.Extension);
            if (storedType != requestedType && settings.Comparers.TryGet(storedType, out var storedComparer))
                return storedComparer;
        }

        return settings.Comparers.Get(requestedType);
    }

    private static string BuildMessage(
        FullPath[] missingPaths,
        FullPath[] extraPaths,
        FullPath[] changedPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Snapshots do not match.");

        var pathsToUpdate = missingPaths.Concat(changedPaths).Distinct().OrderBy(static p => p.Value, StringComparer.Ordinal).ToArray();
        if (pathsToUpdate.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Snapshot file paths:");
            foreach (var path in pathsToUpdate)
            {
                var actualPath = GetActualSnapshotPath(path);
                sb.Append("  * Verified: ").AppendLine(path.Value);
                sb.Append("    Actual:   ").AppendLine(actualPath.Value);
            }
        }

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
                sb.Append("    Actual:   ").AppendLine(GetActualSnapshotPath(path).Value);
            }
        }

        if (changedPaths.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changed snapshot files:");
            foreach (var path in changedPaths.OrderBy(static p => p.Value, StringComparer.Ordinal))
            {
                sb.Append("  * ").AppendLine(path.Value);
            }
        }

        AppendResolutionGuidance(sb);

        return sb.ToString().TrimEnd();
    }

    private static void AppendResolutionGuidance(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Resolution guidance:");
        sb.AppendLine("  - Compare each Verified/Actual pair listed above.");
        sb.AppendLine("  - If the new behavior is correct, copy each .actual file to its .verified file.");
        sb.AppendLine("  - To update snapshots automatically, re-run the test with SNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure).");
        sb.AppendLine("  - If the old behavior is correct, fix the test or production code so output matches the .verified files.");
        sb.AppendLine("  - Remove unexpected .verified files when they are no longer expected.");
        sb.AppendLine("  - Re-run the test.");
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
            var extension = ResolveSnapshotExtension(type, snapshotData);
            var path = settings.SnapshotPathStrategy(new SnapshotPathContext(
                callerContext.SourceFilePath,
                callerContext.ContainingTypeName,
                callerContext.MethodName,
                callerContext.LineNumber,
                type,
                index,
                extension,
                testContext,
                settings,
                serialized.Count));

            result.Add(new SnapshotFile(path, snapshotData));
        }

        return result;
    }

    private static string? ResolveSnapshotExtension(SnapshotType type, SnapshotData snapshotData)
    {
        // A serializer knows the format it actually produced, which may differ from the requested type:
        // an animated GIF is stored as PNG frames, and a source generator result as C# files.
        var extension = string.IsNullOrWhiteSpace(snapshotData.Extension) ? type.Type : snapshotData.Extension;
        return extension?.TrimStart('.');
    }

    private static IReadOnlyCollection<FullPath> DiscoverExpectedFilePaths(List<SnapshotFile> actualFiles)
    {
        var actualPaths = new HashSet<FullPath>(actualFiles.Select(f => f.FilePath));
        if (actualFiles.Count == 0)
            return actualPaths;

        var firstFile = actualFiles[0].FilePath;
        var directory = firstFile.Parent;

        // Exists and LastWriteTimeUtc are served by a single stat of the directory.
        var directoryInfo = new DirectoryInfo(directory);
        if (!directoryInfo.Exists)
            return actualPaths.Where(path => File.Exists(path.Value)).ToArray();

        var firstName = GetVerifiedBaseName(firstFile);
        if (firstName is null)
            return actualPaths.Where(path => File.Exists(path.Value)).ToArray();

        var snapshotName = GetSnapshotName(firstName, actualFiles.Count);
        var indexedPrefix = snapshotName + "_";

        var expected = new HashSet<FullPath>(actualPaths);
        Dictionary<int, string>? indexedCandidates = null;
        foreach (var candidate in GetVerifiedSnapshotFiles(directory, directoryInfo.LastWriteTimeUtc))
        {
            if (string.Equals(candidate.BaseName, snapshotName, StringComparison.Ordinal))
            {
                // The file without an index is this assertion's own snapshot when it produces a single one,
                // and the file it left behind when it used to produce a single one and now produces several.
                expected.Add(directory / candidate.FileName);
            }
            else if (TryGetSnapshotIndex(candidate.BaseName, indexedPrefix, out var index))
            {
                (indexedCandidates ??= [])[index] = candidate.FileName;
            }
        }

        if (indexedCandidates is not null)
        {
            AddIndexedFilesOfThisAssertion(actualFiles, directory, indexedPrefix, indexedCandidates, expected);
        }

        return expected;
    }

    /// <summary>
    /// Adds the indexed files that this assertion wrote in an earlier run, when it produced more snapshots
    /// than it does now.
    /// </summary>
    /// <remarks>
    /// An assertion numbers its files from zero without a gap, so an index only identifies it when every
    /// index below belongs to it too. A lone <c>Name_1.verified.txt</c> sitting next to
    /// <c>Name.verified.txt</c> with no <c>Name_0.verified.txt</c> is the snapshot of a test called
    /// <c>Name_1</c>: reporting it here would fail this assertion for a file it does not own, and deleting it
    /// would take out the other test's baseline.
    /// </remarks>
    private static void AddIndexedFilesOfThisAssertion(
        List<SnapshotFile> actualFiles,
        FullPath directory,
        string indexedPrefix,
        Dictionary<int, string> indexedCandidates,
        HashSet<FullPath> expected)
    {
        var actualIndexes = new HashSet<int>();
        foreach (var actualFile in actualFiles)
        {
            var baseName = GetVerifiedBaseName(actualFile.FilePath);
            if (baseName is not null && TryGetSnapshotIndex(baseName, indexedPrefix, out var actualIndex))
            {
                actualIndexes.Add(actualIndex);
            }
        }

        for (var index = 0; ; index++)
        {
            if (actualIndexes.Contains(index))
                continue;

            if (!indexedCandidates.TryGetValue(index, out var fileName))
                break;

            expected.Add(directory / fileName);
        }
    }

    /// <summary>
    /// Lists the verified snapshot files of a directory. Enumerating the directory is the most expensive
    /// part of an assertion that has nothing to compare yet, and its cost grows with the number of
    /// snapshots stored next to each other, so the result is cached.
    /// </summary>
    /// <remarks>
    /// The cache is dropped when the engine itself adds or removes a verified file, and the directory
    /// timestamp is used as a backstop so a change made by anything else is picked up too. Adding or
    /// removing a directory entry updates that timestamp; editing the content of a file does not, which
    /// is what we want since only the set of file names is cached.
    /// </remarks>
    private static VerifiedSnapshotFile[] GetVerifiedSnapshotFiles(FullPath directory, DateTime lastWriteTimeUtc)
    {
        if (VerifiedSnapshotFiles.TryGetValue(directory, out var cache) && cache.LastWriteTimeUtc == lastWriteTimeUtc)
            return cache.Files;

        var result = new List<VerifiedSnapshotFile>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(path.AsSpan());
            var baseName = GetVerifiedBaseName(fileName);
            if (baseName is null)
                continue;

            result.Add(new VerifiedSnapshotFile(fileName.ToString(), baseName));
        }

        VerifiedSnapshotFile[] files = [.. result];
        VerifiedSnapshotFiles[directory] = new VerifiedSnapshotFileCache(lastWriteTimeUtc, files);
        return files;
    }

    private static void InvalidateVerifiedSnapshotFiles(FullPath directory) => VerifiedSnapshotFiles.TryRemove(directory, out _);

    private static string? GetVerifiedBaseName(FullPath path) => GetVerifiedBaseName(path.Name.AsSpan());

    private static string? GetVerifiedBaseName(ReadOnlySpan<char> fileName)
    {
        var snapshotName = Path.GetFileNameWithoutExtension(fileName);
        if (!snapshotName.EndsWith(".verified", StringComparison.Ordinal))
            return null;

        return snapshotName[..^".verified".Length].ToString();
    }

    /// <summary>
    /// Recovers the name an assertion stores its snapshots under from the name of its first file. Several
    /// snapshots are stored as <c>Name_0</c>, <c>Name_1</c>, ... while a single one keeps the bare
    /// <c>Name</c>, and finding the files written when the assertion produced a different number of
    /// snapshots starts from that common name.
    /// </summary>
    private static string GetSnapshotName(string firstFileName, int actualFileCount)
    {
        if (actualFileCount > 1)
        {
            var separatorIndex = firstFileName.LastIndexOf('_', StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                var suffix = firstFileName[(separatorIndex + 1)..];
                if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    return firstFileName[..separatorIndex];
            }
        }

        return firstFileName;
    }

    private static bool TryGetSnapshotIndex(string baseName, string indexedPrefix, out int index)
    {
        index = 0;
        if (!baseName.StartsWith(indexedPrefix, StringComparison.Ordinal))
            return false;

        var suffix = baseName.AsSpan(indexedPrefix.Length);

        // '_01' is not how an index is written, and mapping it onto 1 would let two files claim one index.
        if (suffix.Length > 1 && suffix[0] == '0')
            return false;

        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static List<SnapshotFileToUpdate> BuildSnapshotFilesToUpdate(IReadOnlyList<SnapshotFile> actualFiles, IReadOnlyCollection<FullPath> pathsToUpdate)
    {
        if (pathsToUpdate.Count == 0)
            return [];

        var actualByPath = actualFiles.ToDictionary(static item => item.FilePath, static item => item.Data.Data);
        var result = new List<SnapshotFileToUpdate>(pathsToUpdate.Count);
        foreach (var path in pathsToUpdate)
        {
            if (!actualByPath.TryGetValue(path, out var data))
                continue;

            result.Add(new SnapshotFileToUpdate(path, GetActualSnapshotPath(path), data));
        }

        return result;
    }

    private static void ApplySnapshotUpdates(SnapshotSettings settings, IReadOnlyList<SnapshotFileToUpdate> filesToUpdate, IReadOnlyCollection<FullPath> filesToDelete)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filesToUpdate);
        ArgumentNullException.ThrowIfNull(filesToDelete);

        // A strategy that needs the verified file to exist - a merge tool needs two sides to compare - creates
        // it itself, so that it also owns removing it when the update does not happen. The engine only makes
        // sure the directory is there, as MoveFile does not create it.
        foreach (var fileToUpdate in filesToUpdate)
        {
            fileToUpdate.VerifiedPath.CreateParentDirectory();
        }

        try
        {
            settings.SnapshotUpdateStrategy.UpdateFiles(
                settings,
                [.. filesToUpdate.Select(item => new SnapshotUpdateFile(item.VerifiedPath, item.ActualPath))],
                [.. filesToDelete.Select(item => item.Value)]);
        }
        finally
        {
            // A strategy that fails part way through has still changed the set of verified files.
            foreach (var fileToUpdate in filesToUpdate)
            {
                InvalidateVerifiedSnapshotFiles(fileToUpdate.VerifiedPath.Parent);
            }

            foreach (var fileToDelete in filesToDelete)
            {
                InvalidateVerifiedSnapshotFiles(fileToDelete.Parent);
            }
        }
    }

    private static void WriteActualSnapshots(IReadOnlyList<SnapshotFileToUpdate> filesToUpdate)
    {
        foreach (var fileToUpdate in filesToUpdate)
        {
            WriteAllBytesWithRetry(fileToUpdate.ActualPath, fileToUpdate.ActualData);
        }
    }

    private static FullPath GetActualSnapshotPath(FullPath expectedSnapshotPath)
    {
        var snapshotName = expectedSnapshotPath.NameWithoutExtension;
        var actualSnapshotName = snapshotName.EndsWith(".verified", StringComparison.Ordinal)
            ? snapshotName[..^".verified".Length] + ".actual"
            : snapshotName + ".actual";

        var extension = expectedSnapshotPath.Extension;
        if (extension.Length == 0)
            return expectedSnapshotPath.Parent / actualSnapshotName;

        return expectedSnapshotPath.Parent / (actualSnapshotName + extension);
    }

    private static void WriteAllBytesWithRetry(FullPath path, byte[] data)
    {
        const int MaxAttemptCount = 8;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                path.CreateParentDirectory();
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    fileInfo.TrySetReadOnly(false);
                }

                File.WriteAllBytes(path, data);
                return;
            }
            catch (IOException) when (attempt < MaxAttemptCount)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(30 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < MaxAttemptCount)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(30 * attempt));
            }
        }
    }

    [DoesNotReturn]
    private static void ThrowAssertion(string message)
    {
        throw new SnapshotAssertionException(message);
    }

    private readonly record struct SnapshotComparisonResult(
        bool HasDifferences,
        string Message,
        string? ExpectedSummary,
        string? ActualSummary,
        IReadOnlyCollection<FullPath> ChangedPaths,
        IReadOnlyCollection<FullPath> MissingPaths,
        IReadOnlyCollection<FullPath> ExtraPaths,
        IReadOnlyCollection<FullPath> PathsToUpdate)
    {
        public static SnapshotComparisonResult NoDifference { get; } = new(false, "", null, null, [], [], [], []);
    }

    private readonly record struct SnapshotFileToUpdate(FullPath VerifiedPath, FullPath ActualPath, byte[] ActualData);

    private readonly record struct VerifiedSnapshotFile(string FileName, string BaseName);

    private readonly record struct VerifiedSnapshotFileCache(DateTime LastWriteTimeUtc, VerifiedSnapshotFile[] Files);
}
