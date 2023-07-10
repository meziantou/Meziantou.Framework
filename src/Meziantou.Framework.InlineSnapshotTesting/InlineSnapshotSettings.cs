using System.Text;
using DiffEngine;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

public sealed record InlineSnapshotSettings
{
    public static InlineSnapshotSettings Default { get; set; } = new();

    public string? Indentation { get; set; }
    public string? EndOfLine { get; set; }
    public Encoding? FileEncoding { get; set; }

    public bool AutoDetectContinuousEnvironment { get; set; } = true;
    public SnapshotUpdateStrategy SnapshotUpdateStrategy { get; set; } = SnapshotUpdateStrategy.Default;
    public SnapshotSerializer SnapshotSerializer { get; set; } = HumanReadableSnapshotSerializer.DefaultInstance;
    public SnapshotComparer SnapshotComparer { get; set; } = SnapshotComparer.Default;
    public AssertionMessageFormatter ErrorMessageFormatter { get; set; } = InlineDiffAssertionMessageFormatter.Instance;
    public AssertionExceptionBuilder AssertionExceptionCreator { get; set; } = new AssertionExceptionBuilder();
    public CSharpStringFormats AllowedStringFormats { get; set; } = CSharpStringFormats.Default;

    /// <summary>
    /// Set the tool to diff snapshots.
    /// If null, the diff tool is determined by
    /// <list type="bullet">
    ///   <item>The <c>DiffEngine_Tool</c> environment variable</item>
    ///   <item>The current IDE (VS, VSCode, Rider)</item>
    /// </list>
    /// </summary>
    /// <remarks>The <c>DiffEngine_Disabled</c> environment variable disable all diff tool even if set explicitly</remarks>
    public DiffTool? MergeTool { get; set; }

    /// <summary>
    /// Before editing a file, use the PDB to validate the file path containing the snapshot.
    /// </summary>
    public bool ValidateSourceFilePathUsingPdbInfoWhenAvailable { get; set; } = true;

    /// <summary>
    /// Before editing a file, use the PDB to validate the line number containing the snapshot.
    /// </summary>
    public bool ValidateLineNumberUsingPdbInfoWhenAvailable { get; set; } = true;

    /// <summary>
    /// Update snapshots even when the snapshot is already valid.
    /// This can be use to reformat snapshots.
    /// </summary>
    public bool ForceUpdateSnapshots { get; set; }

    [DoesNotReturn]
    internal void Assert(string? expected, string? actual)
    {
        var errorMessage = "Snapshots do not match:\n" + ErrorMessageFormatter.FormatMessage(expected, actual);
        throw AssertionExceptionCreator.CreateException(errorMessage);
    }
}
