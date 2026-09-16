using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Meziantou.Framework.SnapshotTesting.Utils;

namespace Meziantou.Framework.SnapshotTesting;

internal static class SnapshotEngine
{
    private static readonly UTF8Encoding Utf8WithoutBomExceptionFallback = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly ConcurrentDictionary<FullPath, VerifiedSnapshotFileCache> VerifiedSnapshotFiles = new();

    // The actual files this process wrote, so it can tell its own leftovers from the files another process just wrote.
    private static readonly ConcurrentDictionary<FullPath, WrittenActualFile> WrittenActualFiles = new();
    private static readonly DateTime StaleActualFileTimeLimitUtc = GetStaleActualFileTimeLimitUtc();

    public static void Validate(SnapshotType? type, object? value, SnapshotSettings settings, string? filePath, int lineNumber, string? memberName, SnapshotTestContext? testContext)
    {
        ArgumentNullException.ThrowIfNull(settings);

        type ??= SnapshotType.Default;
        testContext ??= SnapshotTestContext.Get();
        var callerContext = SnapshotCallerContext.Create(filePath, lineNumber, memberName, testContext);
        var serialized = Serialize(settings, type, value);

        if (serialized is null || serialized.Count == 0)
            throw new SnapshotException("Serializer returned no snapshot data.");

        List<SnapshotFile> actualFiles;
        SnapshotNameOwner? owner;
        try
        {
            actualFiles = BuildActualFiles(settings, callerContext, type, serialized, testContext, out owner);
        }
        finally
        {
            // The strategies are the only consumers of the call stack, and they have all run by now.
            callerContext.Freeze();
        }

        var expectedFilePaths = DiscoverExpectedFilePaths(actualFiles, owner, out var ambiguousFilePaths, out var filesWithADifferentCase);
        var caseReport = RenameFilesWithADifferentCase(settings, callerContext, filesWithADifferentCase);
        var expectedFiles = LoadSnapshotFiles(expectedFilePaths);

        var comparison = Compare(settings, type, actualFiles, expectedFiles);
        var filesToUpdate = BuildSnapshotFilesToUpdate(actualFiles, comparison.PathsToUpdate);
        DeleteStaleActualSnapshots(actualFiles, comparison, ambiguousFilePaths);
        WriteActualSnapshots(filesToUpdate);

        if (!comparison.HasDifferences && !settings.ForceUpdateSnapshots)
        {
            ThrowCaseReport(settings, caseReport);
            return;
        }

        if (!settings.SnapshotUpdateStrategy.CanUpdateSnapshotInternal(settings, callerContext.SourceFilePath, comparison.ExpectedSummary, comparison.ActualSummary))
        {
            if (comparison.HasDifferences)
            {
                ThrowAssertion(AppendUpdatesDisabledReason(settings, AppendCaseReport(comparison.Message, caseReport)));
            }

            ThrowCaseReport(settings, caseReport);
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

        ApplySnapshotUpdates(settings, filesToUpdate, ExcludeAmbiguousPaths(comparison.ExtraPaths, ambiguousFilePaths));

        if (comparison.HasDifferences && settings.SnapshotUpdateStrategy.MustReportError(settings, callerContext.SourceFilePath))
        {
            ThrowAssertion(AppendCaseReport(comparison.Message, caseReport));
        }

        ThrowCaseReport(settings, caseReport);
    }

    /// <summary>
    /// Renames the verified files whose name differs from the name of the snapshot by case only, when the strategy allows
    /// updates, and returns the difference to report, if any.
    /// </summary>
    /// <remarks>
    /// Renaming a test by case only - <c>ValidateUrl</c> to <c>ValidateURL</c> - leaves the file named after the old
    /// name. On a case-insensitive file system (the default on Windows and macOS) the file is still found and the test
    /// passes, and on Linux the snapshot is missing. The file is renamed when the strategy allows updates; otherwise the
    /// difference is reported, even when the content matches, so it is not only noticed on a case-sensitive CI agent.
    /// </remarks>
    private static CaseReport? RenameFilesWithADifferentCase(SnapshotSettings settings, SnapshotCallerContext callerContext, IReadOnlyList<SnapshotFileRename> files)
    {
        if (files.Count == 0)
            return null;

        var existingSummary = FormatSummary(files.Select(static file => file.ExistingPath));
        var expectedSummary = FormatSummary(files.Select(static file => file.ExpectedPath));
        if (!settings.SnapshotUpdateStrategy.CanUpdateSnapshotInternal(settings, callerContext.SourceFilePath, existingSummary, expectedSummary))
            return new CaseReport(BuildCaseMessage(files, renamed: false), Renamed: false);

        foreach (var file in files)
        {
            RenameFileCase(file.ExistingPath, file.ExpectedPath);
        }

        if (!settings.SnapshotUpdateStrategy.MustReportError(settings, callerContext.SourceFilePath))
            return null;

        return new CaseReport(BuildCaseMessage(files, renamed: true), Renamed: true);
    }

    private static void RenameFileCase(FullPath existingPath, FullPath expectedPath)
    {
        // A case-insensitive file system may refuse to rename a file to its own name with a different case, so the
        // file goes through a temporary name. That name matches neither the verified nor the actual naming pattern.
        var temporaryPath = existingPath.Parent / ("." + Guid.NewGuid().ToString("N") + ".snapshot.tmp");
        try
        {
            File.Move(existingPath, temporaryPath);
        }
        catch (FileNotFoundException)
        {
            // Another process validating the same snapshot renamed it first.
            return;
        }
        finally
        {
            InvalidateVerifiedSnapshotFiles(existingPath.Parent);
        }

        try
        {
            File.Move(temporaryPath, expectedPath);
        }
        catch
        {
            try
            {
                File.Move(temporaryPath, existingPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    private static string BuildCaseMessage(IReadOnlyList<SnapshotFileRename> files, bool renamed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(renamed
            ? "Snapshot files were renamed, as their name differed from the snapshot name by case only:"
            : "Snapshot file names differ from the snapshot name by case only:");
        foreach (var file in files.OrderBy(static file => file.ExpectedPath.Value, StringComparer.Ordinal))
        {
            sb.Append("  * ").Append(file.ExistingPath.Value).Append(" => ").AppendLine(file.ExpectedPath.Name);
        }

        if (!renamed)
        {
            sb.AppendLine();
            sb.AppendLine("On a case-insensitive file system (the default on Windows and macOS) the snapshot is still found, but on a case-sensitive one (Linux) it is missing.");
            sb.AppendLine("Rename each file, or re-run the test with SNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure) to rename them automatically.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string AppendCaseReport(string message, CaseReport? caseReport)
    {
        if (caseReport is null)
            return message;

        return message + Environment.NewLine + Environment.NewLine + caseReport.Value.Message;
    }

    private static void ThrowCaseReport(SnapshotSettings settings, CaseReport? caseReport)
    {
        if (caseReport is not { } report)
            return;

        ThrowAssertion(report.Renamed ? report.Message : AppendUpdatesDisabledReason(settings, report.Message));
    }

    private static IReadOnlyList<SnapshotData> Serialize(SnapshotSettings settings, SnapshotType type, object? value)
    {
        var data = settings.Serializers.Serialize(type, value).Data;
        if (settings.Scrubbers.Count == 0)
            return data;

        var result = new List<SnapshotData>(data.Count);
        foreach (var snapshotData in data)
        {
            result.Add(ApplyScrubbers(type, snapshotData, settings.Scrubbers));
        }

        return result;
    }

    /// <summary>
    /// Applies the scrubbers to a text snapshot. Scrubbers work on lines of text, so a snapshot stored in a binary
    /// format (<c>png</c>, <c>bin</c>, ...) is left untouched, even when its bytes happen to be valid UTF-8. A format
    /// that is neither a known text format nor a known binary one is scrubbed when its content is UTF-8 text.
    /// </summary>
    private static SnapshotData ApplyScrubbers(SnapshotType type, SnapshotData snapshotData, IList<Scrubber> scrubbers)
    {
        var extension = ResolveSnapshotExtension(type, snapshotData);
        if (SnapshotType.IsKnownBinaryType(extension))
            return snapshotData;

        string text;
        try
        {
            text = Utf8WithoutBomExceptionFallback.GetString(snapshotData.Data);
        }
        catch (DecoderFallbackException ex)
        {
            if (!SnapshotType.IsKnownTextType(extension))
                return snapshotData;

            throw new SnapshotException($"Snapshot scrubbers can only be applied to UTF-8 text snapshots, and the '{extension}' snapshot is not valid UTF-8.", ex);
        }

        // Scrubbers work on lines, and a byte order mark would be part of the first one: a pattern anchored at the start
        // of a line would not match it. The text comparer ignores a leading byte order mark, so it is not restored.
        if (text.StartsWith('\uFEFF', StringComparison.Ordinal))
        {
            text = text[1..];
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
                if (GetComparer(settings, type, actualFile.Data).Equals(expectedData, actualFile.Data, out var mismatchReason))
                    return SnapshotComparisonResult.NoDifference;

                // The two sets hold the same single path, so its content is the only thing that can differ.
                FullPath[] changedPath = [actualFile.FilePath];
                var summary = FormatSummary(changedPath);
                var mismatchReasons = mismatchReason is null ? null : new Dictionary<FullPath, string> { [actualFile.FilePath] = mismatchReason };
                return new SnapshotComparisonResult(HasDifferences: true, BuildMessage([], [], changedPath, mismatchReasons), summary, summary, changedPath, [], [], changedPath);
            }
        }

        var actualByPath = actualFiles.ToDictionary(item => item.FilePath, item => item.Data);
        var expectedPaths = new HashSet<FullPath>(expectedFiles.Keys);
        var actualPaths = new HashSet<FullPath>(actualByPath.Keys);

        var missingPaths = actualPaths.Where(path => !expectedPaths.Contains(path)).ToArray();
        var extraPaths = expectedPaths.Where(path => !actualPaths.Contains(path)).ToArray();

        var changedPaths = new List<FullPath>();
        Dictionary<FullPath, string>? changedPathReasons = null;
        foreach (var path in expectedPaths.Intersect(actualPaths))
        {
            var actualData = actualByPath[path];
            if (!GetComparer(settings, type, actualData).Equals(expectedFiles[path], actualData, out var mismatchReason))
            {
                changedPaths.Add(path);
                if (mismatchReason is not null)
                {
                    (changedPathReasons ??= [])[path] = mismatchReason;
                }
            }
        }

        if (missingPaths.Length == 0 && extraPaths.Length == 0 && changedPaths.Count == 0)
        {
            return SnapshotComparisonResult.NoDifference;
        }

        FullPath[] changedPathArray = [.. changedPaths];
        var message = BuildMessage(missingPaths, extraPaths, changedPathArray, changedPathReasons);
        var pathsToUpdate = missingPaths.Concat(changedPaths).Distinct().ToArray();
        return new SnapshotComparisonResult(HasDifferences: true, message, FormatSummary(expectedPaths), FormatSummary(actualPaths), changedPathArray, missingPaths, extraPaths, pathsToUpdate);
    }

    /// <summary>
    /// Resolves the comparer for a snapshot file. A serializer may store a different format than the one that
    /// was requested - an animated GIF becomes PNG frames, a source generator result becomes C# files - and
    /// the comparison is about the bytes that ended up on disk, so a comparer registered for the stored format
    /// wins. The requested type stays as the fallback, so an explicit registration for it still applies when
    /// nothing is registered for the stored format.
    /// <para>
    /// The PNG frames of a GIF or ICO snapshot are encoded by this library, and the compressed bytes depend on the
    /// zlib of the runtime while the pixels do not. When no comparer is registered for either format, they are
    /// compared by pixels, so a runtime update does not change the outcome.
    /// </para>
    /// </summary>
    private static ISnapshotComparer GetComparer(SnapshotSettings settings, SnapshotType requestedType, SnapshotData data)
    {
        if (!string.IsNullOrEmpty(data.Extension))
        {
            var storedType = SnapshotType.Create(data.Extension);
            if (storedType != requestedType)
            {
                if (settings.Comparers.TryGet(storedType, out var storedComparer))
                    return storedComparer;

                if (storedType == SnapshotType.Png && (requestedType == SnapshotType.Gif || requestedType == SnapshotType.Ico) && !settings.Comparers.TryGet(requestedType, out _))
                    return ImageComparer.Instance;
            }
        }

        return settings.Comparers.Get(requestedType);
    }

    private static string BuildMessage(
        FullPath[] missingPaths,
        FullPath[] extraPaths,
        FullPath[] changedPaths,
        Dictionary<FullPath, string>? mismatchReasons)
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
                // Nothing produces this file anymore, so there is no actual file to compare it with.
                sb.Append("  - ").AppendLine(path.Value);
            }
        }

        if (changedPaths.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changed snapshot files:");
            foreach (var path in changedPaths.OrderBy(static p => p.Value, StringComparer.Ordinal))
            {
                sb.Append("  * ").AppendLine(path.Value);

                // The comparer's explanation, such as a similarity score that did not reach its threshold
                if (mismatchReasons is not null && mismatchReasons.TryGetValue(path, out var mismatchReason))
                {
                    sb.Append("    ").AppendLine(mismatchReason);
                }
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

    /// <summary>
    /// Explains that the snapshot was not updated because the environment detection disabled updates. Without it, the
    /// guidance suggests re-running with an update strategy that the detection silently ignores.
    /// </summary>
    private static string AppendUpdatesDisabledReason(SnapshotSettings settings, string message)
    {
        if (!settings.AutoDetectContinuousEnvironment || ContinuousEnvironmentDetector.GetDetectedEnvironmentDescription() is not { } environment)
            return message;

        return message + Environment.NewLine + Environment.NewLine + ContinuousEnvironmentDetector.FormatUpdatesDisabledMessage(environment, SnapshotSettings.AutoDetectContinuousEnvironmentVariableName, nameof(SnapshotSettings));
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

            // An extension may contain dots ('g.cs'), so it starts after the marker rather than at the last dot.
            string extension;
            if (SnapshotFileName.TryParseVerified(path.Name, out _, out var verifiedExtension))
            {
                extension = verifiedExtension.ToString();
            }
            else
            {
                extension = path.Extension;
                if (extension.Length > 0 && extension[0] == '.')
                {
                    extension = extension[1..];
                }
            }

            // Another process validating the same snapshot may be replacing the file right now
            byte[] content;
            try
            {
                content = SnapshotUpdateStrategy.ReadAllBytesWithRetry(path);
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            result[path] = new SnapshotData(extension, content);
        }

        return result;
    }

    private static List<SnapshotFile> BuildActualFiles(
        SnapshotSettings settings,
        SnapshotCallerContext callerContext,
        SnapshotType type,
        IReadOnlyList<SnapshotData> serialized,
        SnapshotTestContext? testContext,
        out SnapshotNameOwner? owner)
    {
        var result = BuildActualFilesForTestContext(settings, callerContext, type, serialized, testContext);
        var usesBuiltInNames = SnapshotSettings.UsesBuiltInSnapshotNames(settings);
        owner = usesBuiltInNames ? callerContext.GetNameOwner() : null;
        var isLegacyName = false;

        // Earlier versions derived some test names in ways that gave several tests the same name. The snapshot
        // files they created keep being used while the files for the current name do not exist, so the new naming
        // does not orphan the snapshots users already committed. The legacy name may be the current name of
        // another test - the string "null" was named like null - in which case that test owns the file.
        if (testContext?.LegacyTestName is { } legacyTestName && !result.Exists(static file => File.Exists(file.FilePath)))
        {
            var legacyResult = BuildActualFilesForTestContext(settings, callerContext, type, serialized, testContext with { TestName = legacyTestName, LegacyTestName = null });
            if (legacyResult.Exists(static file => File.Exists(file.FilePath)) &&
                (owner is null || GetSnapshotName(legacyResult) is not { } legacySnapshotName || !SnapshotNameRegistry.IsClaimedByAnotherTest(legacyResult[0].FilePath.Parent, legacySnapshotName, owner)))
            {
                result = legacyResult;
                isLegacyName = true;
            }
        }

        if (owner is not null && GetSnapshotName(result) is { } snapshotName)
        {
            var directory = result[0].FilePath.Parent;
            SnapshotNameRegistry.Claim(directory, snapshotName, owner, isLegacyName);

            // The files of an assertion that produces several snapshots are named 'Name_0', 'Name_1', ...: the name of
            // each file is claimed too, so a test called 'Name_0' is reported instead of sharing the first file.
            if (result.Count > 1)
            {
                foreach (var file in result)
                {
                    if (file.FilePath.Parent == directory && GetVerifiedBaseName(file.FilePath) is { } fileName && !string.Equals(fileName, snapshotName, StringComparison.Ordinal))
                    {
                        SnapshotNameRegistry.Claim(directory, fileName, owner, isLegacyName);
                    }
                }
            }
        }

        return result;
    }

    private static string? GetSnapshotName(List<SnapshotFile> files)
    {
        if (files.Count == 0 || GetVerifiedBaseName(files[0].FilePath) is not { } firstName)
            return null;

        return GetSnapshotName(firstName, files.Count);
    }

    private static List<SnapshotFile> BuildActualFilesForTestContext(
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
                callerContext,
                type,
                index,
                extension,
                testContext,
                settings,
                serialized.Count));

            // Comparers receive the extension in the same form for both sides: without the leading dot, as the
            // verified file loaded from disk has it.
            if (!string.Equals(snapshotData.Extension, extension, StringComparison.Ordinal))
            {
                snapshotData = snapshotData with { Extension = extension };
            }

            result.Add(new SnapshotFile(path, snapshotData));
        }

        return result;
    }

    private static string ResolveSnapshotExtension(SnapshotType type, SnapshotData snapshotData)
    {
        // A serializer knows the format it actually produced, which may differ from the requested type:
        // an animated GIF is stored as PNG frames, and a source generator result as C# files.
        var extension = string.IsNullOrWhiteSpace(snapshotData.Extension) ? type.Type : snapshotData.Extension;
        extension = extension?.TrimStart('.');

        // Content without a format - SnapshotType.None - is stored as a 'bin' file by the default path strategy. Resolving
        // it here too makes it binary for the scrubbers, and selects the comparer registered for 'bin'.
        return string.IsNullOrWhiteSpace(extension) ? "bin" : extension;
    }

    /// <summary>
    /// Lists the verified files that belong to the assertion: the files it produces, plus the files it left behind
    /// when it produced a different number of snapshots or a different type of snapshot.
    /// </summary>
    /// <param name="actualFiles">Files the assertion produces.</param>
    /// <param name="owner">Test the assertion belongs to, or <see langword="null" /> when its names are not tracked by <see cref="SnapshotNameRegistry" />.</param>
    /// <param name="ambiguousPaths">
    /// Files that are reported as unexpected but must not be deleted, as they may belong to another test. See
    /// <see cref="AddIndexedFilesOfThisAssertion" />.
    /// </param>
    /// <param name="filesWithADifferentCase">
    /// Existing files whose name differs from the name of a file the assertion produces by case only.
    /// </param>
    /// <remarks>
    /// <para>
    /// Every file named after the assertion is claimed, whatever its extension: a test whose snapshot changes type
    /// - a text snapshot becoming a PNG - must not leave its previous file behind. This is sound because another
    /// assertion cannot use the same name: a second assertion of the same test gets an ordinal suffix, and a test
    /// whose name collides with another one is reported by <see cref="SnapshotNameRegistry" />.
    /// </para>
    /// <para>
    /// The exception is a test with several assertions, recognizable by the <c>~2</c>, <c>~3</c>, ... files of its
    /// other assertions. The ordinals follow the order the assertions run, so an assertion that only runs on some
    /// platforms shifts the ones after it: <c>if (OperatingSystem.IsWindows()) Validate(png); Validate(text);</c> names
    /// the text snapshot <c>Name~2</c> on Windows and <c>Name</c> elsewhere. The <c>Name.verified.png</c> file is then
    /// the snapshot of another assertion rather than a file this one left behind, so a file with another extension is
    /// left alone.
    /// </para>
    /// </remarks>
    private static IReadOnlyCollection<FullPath> DiscoverExpectedFilePaths(
        List<SnapshotFile> actualFiles,
        SnapshotNameOwner? owner,
        out IReadOnlyCollection<FullPath> ambiguousPaths,
        out IReadOnlyList<SnapshotFileRename> filesWithADifferentCase)
    {
        ambiguousPaths = [];
        filesWithADifferentCase = [];
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
        var verifiedFiles = GetVerifiedSnapshotFiles(directory, directoryInfo.LastWriteTimeUtc);

        // The names of the files the assertion produces, whatever their case: a file whose name only differs by case is
        // the same file on a case-insensitive file system, so it is either this assertion's file or a file to rename, but
        // never a file it left behind. Deleting it would delete the snapshot itself.
        var actualFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actualExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<SnapshotFileRename>? renames = null;
        foreach (var actualFile in actualFiles)
        {
            if (actualFile.FilePath.Parent != directory)
                continue;

            var actualFileName = actualFile.FilePath.Name;
            actualFileNames.Add(actualFileName);
            if (!SnapshotFileName.TryParseVerified(actualFileName, out var actualName, out var actualExtension))
                continue;

            actualExtensions.Add(actualExtension.ToString());
            if (FindFileWithADifferentCase(verifiedFiles, actualName.ToString(), actualFileName) is { } existingFileName)
            {
                (renames ??= []).Add(new SnapshotFileRename(directory / existingFileName, actualFile.FilePath));
            }
        }

        if (renames is not null)
        {
            filesWithADifferentCase = renames;
        }

        var expected = new HashSet<FullPath>(actualPaths);
        var hasFileWithoutIndex = false;
        if (verifiedFiles.FilesByName.TryGetValue(GetNameWithoutIndex(snapshotName), out var namedCandidates))
        {
            var hasOtherAssertions = HasOtherAssertions(verifiedFiles, snapshotName);
            foreach (var candidate in namedCandidates)
            {
                if (!string.Equals(candidate.BaseName, snapshotName, StringComparison.OrdinalIgnoreCase))
                    continue;

                hasFileWithoutIndex = true;
                if (actualFileNames.Contains(candidate.FileName) || !string.Equals(candidate.BaseName, snapshotName, StringComparison.Ordinal))
                    continue;

                if (hasOtherAssertions && !actualExtensions.Contains(candidate.Extension))
                    continue;

                // The file without an index is this assertion's own snapshot when it produces a single one,
                // and the file it left behind when it used to produce a single one and now produces several.
                expected.Add(directory / candidate.FileName);
            }
        }

        if (verifiedFiles.FilesByName.TryGetValue(snapshotName, out var indexedFiles))
        {
            Dictionary<int, List<string>>? indexedCandidates = null;
            foreach (var candidate in indexedFiles)
            {
                if (actualFileNames.Contains(candidate.FileName))
                    continue;

                if (TryGetSnapshotIndex(candidate.BaseName, indexedPrefix, out var index))
                {
                    indexedCandidates ??= [];
                    if (!indexedCandidates.TryGetValue(index, out var fileNames))
                    {
                        fileNames = [];
                        indexedCandidates[index] = fileNames;
                    }

                    fileNames.Add(candidate.FileName);
                }
            }

            if (indexedCandidates is not null)
            {
                var ambiguous = new HashSet<FullPath>();
                AddIndexedFilesOfThisAssertion(actualFiles, directory, owner, indexedPrefix, hasFileWithoutIndex, indexedCandidates, expected, ambiguous);
                ambiguousPaths = ambiguous;
            }
        }

        return expected;
    }

    /// <summary>
    /// Returns the name of the verified file whose name equals <paramref name="fileName" /> when the case is ignored, but
    /// not otherwise, or <see langword="null" /> when a file has exactly this name or no file has a similar one.
    /// </summary>
    private static string? FindFileWithADifferentCase(VerifiedSnapshotFileCache verifiedFiles, string name, string fileName)
    {
        if (!verifiedFiles.FilesByName.TryGetValue(GetNameWithoutIndex(name), out var candidates))
            return null;

        string? result = null;
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.FileName, fileName, StringComparison.Ordinal))
                return null;

            if (string.Equals(candidate.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                result = candidate.FileName;
            }
        }

        return result;
    }

    /// <summary>
    /// Indicates whether the test of the assertion has other assertions, as the files named with another call ordinal
    /// show. The ordinal comes right after the name (<c>Name~2</c>, <c>Name~2_0</c>), so the first assertion is named
    /// <c>Name</c> and the other ones <c>Name~N</c>.
    /// </summary>
    private static bool HasOtherAssertions(VerifiedSnapshotFileCache verifiedFiles, string snapshotName)
    {
        // A snapshot with an ordinal suffix is not the first assertion of its test, so the test has several.
        if (GetCallOrdinalSeparatorIndex(snapshotName) >= 0)
            return true;

        return verifiedFiles.NamesWithCallOrdinals.Contains(snapshotName);
    }

    /// <summary>
    /// Returns the index of the <c>~</c> that starts the call ordinal suffix of a name, or -1 when the name has none.
    /// </summary>
    private static int GetCallOrdinalSeparatorIndex(ReadOnlySpan<char> name)
    {
        var index = name.LastIndexOf('~');
        if (index <= 0 || index == name.Length - 1)
            return -1;

        foreach (var c in name[(index + 1)..])
        {
            if (!char.IsAsciiDigit(c))
                return -1;
        }

        return index;
    }

    /// <summary>
    /// Adds the indexed files that this assertion wrote in an earlier run, when it produced more snapshots
    /// than it does now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An assertion numbers its files from zero without a gap, so an index only identifies it when every
    /// index below belongs to it too. A lone <c>Name_1.verified.txt</c> sitting next to
    /// <c>Name.verified.txt</c> with no <c>Name_0.verified.txt</c> is the snapshot of a test called
    /// <c>Name_1</c>: reporting it here would fail this assertion for a file it does not own, and deleting it
    /// would take out the other test's baseline.
    /// </para>
    /// <para>
    /// That rule says nothing about index 0, so an assertion that produces a single snapshot only claims the
    /// indexed files while its own <c>Name.verified.*</c> file does not exist yet: that is the run where it went
    /// from several snapshots to one. Once the file exists, <c>Name_0</c>, <c>Name_1</c>, ... are the snapshots of
    /// tests called <c>Name_0</c>, <c>Name_1</c>, ... as often as leftovers - tests that did not run in this process
    /// are invisible - so they are neither reported nor deleted.
    /// </para>
    /// <para>
    /// When <c>Name</c> produces <c>Name_0</c> and <c>Name_1</c>, a <c>Name_2.verified.txt</c> is either a file it
    /// produced in an earlier run or the snapshot of a test called <c>Name_2</c>, and nothing on disk tells which. Such
    /// a file is skipped when another test of the current process uses that name, and otherwise reported without
    /// being deleted: the user decides whether to remove it.
    /// </para>
    /// </remarks>
    private static void AddIndexedFilesOfThisAssertion(
        List<SnapshotFile> actualFiles,
        FullPath directory,
        SnapshotNameOwner? owner,
        string indexedPrefix,
        bool hasFileWithoutIndex,
        Dictionary<int, List<string>> indexedCandidates,
        HashSet<FullPath> expected,
        HashSet<FullPath> ambiguous)
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

        if (actualIndexes.Count == 0 && hasFileWithoutIndex)
            return;

        for (var index = 0; ; index++)
        {
            if (actualIndexes.Contains(index))
                continue;

            if (!indexedCandidates.TryGetValue(index, out var fileNames))
                break;

            var indexedName = indexedPrefix + index.ToString(CultureInfo.InvariantCulture);
            if (owner is null ? SnapshotNameRegistry.IsClaimed(directory, indexedName) : SnapshotNameRegistry.IsClaimedByAnotherTest(directory, indexedName, owner))
                break;

            foreach (var fileName in fileNames)
            {
                var path = directory / fileName;
                expected.Add(path);
                if (actualIndexes.Count > 0)
                {
                    ambiguous.Add(path);
                }
            }
        }
    }

    private static IReadOnlyCollection<FullPath> ExcludeAmbiguousPaths(IReadOnlyCollection<FullPath> paths, IReadOnlyCollection<FullPath> ambiguousPaths)
    {
        if (ambiguousPaths.Count == 0 || paths.Count == 0)
            return paths;

        return [.. paths.Where(path => !ambiguousPaths.Contains(path))];
    }

    /// <summary>
    /// Lists the verified snapshot files of a directory. Enumerating the directory is the most expensive
    /// part of an assertion that has nothing to compare yet, and its cost grows with the number of
    /// snapshots stored next to each other, so the result is cached, and indexed by name so an assertion only
    /// looks at the files named after it.
    /// </summary>
    /// <remarks>
    /// The cache is dropped when the engine itself adds, removes or renames a verified file, and the directory
    /// timestamp is used as a backstop so a change made by anything else is picked up too. Adding or
    /// removing a directory entry updates that timestamp; editing the content of a file does not, which
    /// is what we want since only the set of file names is cached. The engine's own changes to actual files update
    /// the timestamp too, but not the set of verified files: see <see cref="CaptureVerifiedSnapshotFilesTimestamp" />.
    /// </remarks>
    private static VerifiedSnapshotFileCache GetVerifiedSnapshotFiles(FullPath directory, DateTime lastWriteTimeUtc)
    {
        if (VerifiedSnapshotFiles.TryGetValue(directory, out var cache) && cache.LastWriteTimeUtc == lastWriteTimeUtc)
            return cache;

        var filesByName = new Dictionary<string, List<VerifiedSnapshotFile>>(StringComparer.OrdinalIgnoreCase);
        var namesWithCallOrdinals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(path.AsSpan());
            if (!SnapshotFileName.TryParseVerified(fileName, out var name, out var extension))
                continue;

            var baseName = name.ToString();
            var nameWithoutIndex = GetNameWithoutIndex(baseName);
            if (!filesByName.TryGetValue(nameWithoutIndex, out var files))
            {
                files = [];
                filesByName[nameWithoutIndex] = files;
            }

            files.Add(new VerifiedSnapshotFile(fileName.ToString(), baseName, extension.ToString()));

            // The call ordinal comes before the index: 'Name~2' or 'Name~2_0'.
            var ordinalIndex = GetCallOrdinalSeparatorIndex(baseName);
            if (ordinalIndex < 0)
            {
                ordinalIndex = GetCallOrdinalSeparatorIndex(nameWithoutIndex);
            }

            if (ordinalIndex >= 0)
            {
                namesWithCallOrdinals.Add(baseName[..ordinalIndex]);
            }
        }

        var result = new VerifiedSnapshotFileCache(lastWriteTimeUtc, filesByName, namesWithCallOrdinals);
        VerifiedSnapshotFiles[directory] = result;
        return result;
    }

    private static void InvalidateVerifiedSnapshotFiles(FullPath directory) => VerifiedSnapshotFiles.TryRemove(directory, out _);

    /// <summary>
    /// Returns the timestamp of a directory whose cached verified files are up to date, before the engine changes an
    /// actual file in it, or <see langword="null" /> when there is nothing to keep.
    /// </summary>
    /// <remarks>
    /// Writing or deleting an actual file updates the directory timestamp, which would drop the cached listing although
    /// the set of verified files did not change: every failing assertion would then enumerate the directory again.
    /// <see cref="RestoreVerifiedSnapshotFiles" /> gives the cache the new timestamp when it was up to date before the
    /// change. A verified file that another process adds while the actual file is being written is missed until the
    /// directory changes again; the only consequence is that a file left behind is not reported, as the files an
    /// assertion produces are always read from their own path.
    /// </remarks>
    private static DateTime? CaptureVerifiedSnapshotFilesTimestamp(FullPath directory)
    {
        if (!VerifiedSnapshotFiles.TryGetValue(directory, out var cache))
            return null;

        var lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(directory);
        return cache.LastWriteTimeUtc == lastWriteTimeUtc ? lastWriteTimeUtc : null;
    }

    private static void RestoreVerifiedSnapshotFiles(FullPath directory, DateTime? previousLastWriteTimeUtc)
    {
        if (previousLastWriteTimeUtc is not { } previous || !VerifiedSnapshotFiles.TryGetValue(directory, out var cache) || cache.LastWriteTimeUtc != previous)
            return;

        var lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(directory);
        if (lastWriteTimeUtc != previous)
        {
            VerifiedSnapshotFiles.TryUpdate(directory, cache with { LastWriteTimeUtc = lastWriteTimeUtc }, cache);
        }
    }

    private static string? GetVerifiedBaseName(FullPath path)
    {
        if (!SnapshotFileName.TryParseVerified(path.Name, out var name, out _))
            return null;

        return name.ToString();
    }

    /// <summary>
    /// Removes the <c>_&lt;index&gt;</c> suffix a name may have. Whether the suffix is an index or part of the test name
    /// cannot be told from the name alone, so the result is only used to group the files an assertion may own.
    /// </summary>
    private static string GetNameWithoutIndex(string name)
    {
        var separatorIndex = name.LastIndexOf('_', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == name.Length - 1)
            return name;

        foreach (var c in name.AsSpan(separatorIndex + 1))
        {
            if (!char.IsAsciiDigit(c))
                return name;
        }

        return name[..separatorIndex];
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
        // sure the directory is there, as a strategy that only replaces files may not create it.
        foreach (var fileToUpdate in filesToUpdate)
        {
            fileToUpdate.VerifiedPath.CreateParentDirectory();
        }

        // Another process may remove an actual file before the strategy promotes it, so the strategy gets its content
        // from memory in that case.
        var actualFileContents = new Dictionary<string, byte[]>(filesToUpdate.Count, StringComparer.Ordinal);
        foreach (var fileToUpdate in filesToUpdate)
        {
            actualFileContents[fileToUpdate.ActualPath.Value] = fileToUpdate.ActualData;
        }

        var previousActualFileContents = SnapshotUpdateStrategy.SetActualFileContents(actualFileContents);
        try
        {
            settings.SnapshotUpdateStrategy.UpdateFiles(
                settings,
                [.. filesToUpdate.Select(item => new SnapshotUpdateFile(item.VerifiedPath, item.ActualPath))],
                [.. filesToDelete.Select(item => item.Value)]);
        }
        finally
        {
            SnapshotUpdateStrategy.SetActualFileContents(previousActualFileContents);

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
            var directory = fileToUpdate.ActualPath.Parent;
            var cacheTimestamp = CaptureVerifiedSnapshotFilesTimestamp(directory);
            SnapshotUpdateStrategy.WriteAllBytesWithRetry(fileToUpdate.ActualPath, fileToUpdate.ActualData);
            WrittenActualFiles[fileToUpdate.ActualPath] = new WrittenActualFile(fileToUpdate.ActualData.LongLength, XxHash3.HashToUInt64(fileToUpdate.ActualData));
            RestoreVerifiedSnapshotFiles(directory, cacheTimestamp);
        }
    }

    /// <summary>
    /// Removes the actual files that an earlier failed run of this assertion left behind for snapshots that now match,
    /// and for snapshots that the assertion no longer produces. Approving the actual files would otherwise replace a
    /// correct verified file with that stale output, or bring back a snapshot that is no longer expected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The actual file of a verified file that may belong to another test is kept, as the verified file is.
    /// </para>
    /// <para>
    /// A test project that targets several frameworks runs one process per framework, and they validate the same
    /// snapshots at the same time. When the output differs by framework, one process writes the actual file while the
    /// other one finds a matching snapshot: that actual file is not a leftover. So an actual file is only removed when
    /// it predates the current process, or when this process wrote it and nothing replaced it since.
    /// </para>
    /// </remarks>
    private static void DeleteStaleActualSnapshots(List<SnapshotFile> actualFiles, SnapshotComparisonResult comparison, IReadOnlyCollection<FullPath> ambiguousPaths)
    {
        foreach (var actualFile in actualFiles)
        {
            if (comparison.PathsToUpdate.Contains(actualFile.FilePath))
                continue;

            DeleteStaleActualSnapshot(actualFile.FilePath);
        }

        foreach (var extraPath in comparison.ExtraPaths)
        {
            if (ambiguousPaths.Contains(extraPath))
                continue;

            DeleteStaleActualSnapshot(extraPath);
        }
    }

    private static void DeleteStaleActualSnapshot(FullPath verifiedPath)
    {
        var actualPath = GetActualSnapshotPath(verifiedPath);
        var fileInfo = new FileInfo(actualPath);
        if (!fileInfo.Exists || !IsStaleActualFile(actualPath, fileInfo))
            return;

        var directory = actualPath.Parent;
        var cacheTimestamp = CaptureVerifiedSnapshotFilesTimestamp(directory);
        SnapshotUpdateStrategy.TryDeleteFile(actualPath);
        WrittenActualFiles.TryRemove(actualPath, out _);
        RestoreVerifiedSnapshotFiles(directory, cacheTimestamp);
    }

    private static bool IsStaleActualFile(FullPath actualPath, FileInfo fileInfo)
    {
        if (fileInfo.LastWriteTimeUtc < StaleActualFileTimeLimitUtc)
            return true;

        if (!WrittenActualFiles.TryGetValue(actualPath, out var writtenFile) || writtenFile.Length != fileInfo.Length)
            return false;

        try
        {
            return XxHash3.HashToUInt64(SnapshotUpdateStrategy.ReadAllBytesWithRetry(actualPath)) == writtenFile.Hash;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the time before which an actual file was written by an earlier run rather than by a process running now.
    /// </summary>
    /// <remarks>
    /// File timestamps and the process start time do not come from the same clock reading, and some file systems store
    /// timestamps with a coarse precision (2 seconds for FAT), so a margin is taken. It errs on the side of keeping the
    /// file: a leftover that is kept is overwritten or removed by a later run, whereas a file that is deleted by mistake
    /// is the output another process just wrote.
    /// </remarks>
    private static DateTime GetStaleActualFileTimeLimitUtc()
    {
        var margin = TimeSpan.FromSeconds(2);
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime() - margin;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The start time is not available on every platform. The first assertion comes later than the process start,
            // so a file written in between by another process may be taken for a leftover.
            return DateTime.UtcNow - margin;
        }
    }

    private static FullPath GetActualSnapshotPath(FullPath expectedSnapshotPath)
    {
        // An extension may contain dots ('g.cs'), so the actual file name is built from the marker rather than from the
        // last dot: 'Name.verified.g.cs' becomes 'Name.actual.g.cs', as the approval tool expects.
        if (SnapshotFileName.GetActualFileName(expectedSnapshotPath.Name) is { } actualFileName)
            return expectedSnapshotPath.Parent / actualFileName;

        var snapshotName = expectedSnapshotPath.NameWithoutExtension;
        var actualSnapshotName = snapshotName.EndsWith(".verified", StringComparison.Ordinal)
            ? snapshotName[..^".verified".Length] + ".actual"
            : snapshotName + ".actual";

        var extension = expectedSnapshotPath.Extension;
        if (extension.Length == 0)
            return expectedSnapshotPath.Parent / actualSnapshotName;

        return expectedSnapshotPath.Parent / (actualSnapshotName + extension);
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

    private readonly record struct SnapshotFileRename(FullPath ExistingPath, FullPath ExpectedPath);

    private readonly record struct CaseReport(string Message, bool Renamed);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct WrittenActualFile(long Length, ulong Hash);

    private readonly record struct VerifiedSnapshotFile(string FileName, string BaseName, string Extension);

    /// <param name="LastWriteTimeUtc">Timestamp of the directory when it was listed.</param>
    /// <param name="FilesByName">Verified files, by name without the <c>_&lt;index&gt;</c> suffix, whatever its case.</param>
    /// <param name="NamesWithCallOrdinals">Names that have files with a <c>~&lt;ordinal&gt;</c> suffix, without the suffix.</param>
    private readonly record struct VerifiedSnapshotFileCache(DateTime LastWriteTimeUtc, Dictionary<string, List<VerifiedSnapshotFile>> FilesByName, HashSet<string> NamesWithCallOrdinals);
}
