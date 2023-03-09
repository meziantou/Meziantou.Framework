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
    public SnapshotSerializer SnapshotSerializer { get; set; } = ReadableSnapshotSerializer.Instance;
    public SnapshotComparer SnapshotComparer { get; set; } = SnapshotComparer.Default;
    public AssertionMessageFormatter ErrorMessageFormatter { get; set; } = InlineDiffAssertionMessageFormatter.Instance;
    public AssertionExceptionBuilder AssertionExceptionCreator { get; set; } = new AssertionExceptionBuilder();
    public CSharpStringFormats AllowedStringFormats { get; set; } = CSharpStringFormats.Default;
    public DiffTool? MergeTool { get; set; }
    public bool ValidateSourceFilePathUsingPdbInfoWhenAvailable { get; set; } = true;
    public bool ValidateLineNumberUsingPdbInfoWhenAvailable { get; set; } = true;

    [DoesNotReturn]
    internal void Assert(string expected, string actual)
    {
        var errorMessage = "Snapshots do not match:\n" + ErrorMessageFormatter.FormatMessage(expected, actual);
        throw AssertionExceptionCreator.CreateException(errorMessage);
    }
}
