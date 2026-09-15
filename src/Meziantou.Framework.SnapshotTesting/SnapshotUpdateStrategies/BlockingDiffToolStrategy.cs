namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class BlockingDiffToolStrategy : MergeToolStrategyBase
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    public override void UpdateFile(SnapshotSettings settings, string verifiedFilePath, string actualFilePath)
    {
        var placeholder = VerifiedFilePlaceholder.TryCreate(verifiedFilePath);
        MergeToolResult? result = null;
        try
        {
            result = TryLaunchMergeTool(settings, verifiedFilePath, actualFilePath, waitForMerge: true);
            result?.WaitForExit();
        }
        catch
        {
            result?.Dispose();
            placeholder?.DeleteIfUnused();
            throw;
        }

        if (result is null)
        {
            // Merge tools are switched off: the assertion failure reports the difference.
            placeholder?.DeleteIfUnused();
            return;
        }

        using (result)
        {
            if (!result.WaitsForMerge)
            {
                // The launcher may have handed the files over to a running IDE and exited while the diff is still
                // open, so the placeholder the developer may still save to is only cleaned up with the process.
                placeholder?.DeleteOnProcessExitIfUnused();
                return;
            }
        }

        // The merge tool has exited, so a placeholder that is still untouched means the developer closed
        // the tool without saving.
        placeholder?.DeleteIfUnused();
        DeleteActualFileIfAccepted(verifiedFilePath, actualFilePath);
    }

    /// <summary>
    /// The actual file has been consumed once the developer saved it as the verified file. Otherwise it is kept, as
    /// the assertion failure points to it.
    /// </summary>
    private static void DeleteActualFileIfAccepted(string verifiedFilePath, string actualFilePath)
    {
        try
        {
            var verifiedFile = new FileInfo(verifiedFilePath);
            var actualFile = new FileInfo(actualFilePath);
            if (!verifiedFile.Exists || !actualFile.Exists || verifiedFile.Length != actualFile.Length)
                return;

            if (!File.ReadAllBytes(verifiedFilePath).AsSpan().SequenceEqual(File.ReadAllBytes(actualFilePath)))
                return;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        TryDeleteFile(actualFilePath);
    }
}
