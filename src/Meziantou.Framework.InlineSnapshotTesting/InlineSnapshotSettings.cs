using System.Collections.Immutable;
using System.Text;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

public sealed record InlineSnapshotSettings
{
    private static readonly ImmutableArray<MergeTool> DefaultMergeTools = ImmutableArray.Create<MergeTool>(
        MergeTool.DiffToolFromEnvironmentVariable,
        MergeTool.GitMergeTool,
        MergeTool.GitDiffTool,
        MergeTool.VisualStudioMergeIfCurrentProcess,
        MergeTool.VisualStudioCodeIfCurrentProcess,
        MergeTool.RiderIfCurrentProcess,
        new AutoDiffEngineTool());

    private SnapshotUpdateStrategy _snapshotUpdateStrategy = SnapshotUpdateStrategy.Default;
    private SnapshotSerializer _snapshotSerializer = HumanReadableSnapshotSerializer.DefaultInstance;
    private SnapshotComparer _snapshotComparer = SnapshotComparer.Default;
    private AssertionMessageFormatter _errorMessageFormatter = InlineDiffAssertionMessageFormatter.Instance;
    private AssertionExceptionBuilder _assertionExceptionCreator = AssertionExceptionBuilder.Default;

    public static InlineSnapshotSettings Default { get; set; } = new();

    public string? Indentation { get; set; }
    public string? EndOfLine { get; set; }
    public Encoding? FileEncoding { get; set; }

    public bool AutoDetectContinuousEnvironment { get; set; } = true;
    public CSharpStringFormats AllowedStringFormats { get; set; } = CSharpStringFormats.Default;

    public SnapshotUpdateStrategy SnapshotUpdateStrategy
    {
        get => _snapshotUpdateStrategy;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            _snapshotUpdateStrategy = value;
        }
    }

    public SnapshotSerializer SnapshotSerializer
    {
        get => _snapshotSerializer;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            _snapshotSerializer = value;
        }
    }

    public SnapshotComparer SnapshotComparer
    {
        get => _snapshotComparer;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            _snapshotComparer = value;
        }
    }

    public AssertionMessageFormatter ErrorMessageFormatter
    {
        get => _errorMessageFormatter;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            _errorMessageFormatter = value;
        }
    }

    public AssertionExceptionBuilder AssertionExceptionCreator
    {
        get => _assertionExceptionCreator;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            _assertionExceptionCreator = value;
        }
    }

    public IList<Scrubber> Scrubbers { get; }

    /// <summary>
    /// Set the ordered list of tools to diff snapshots.
    /// If null or empty, the diff tool is determined by
    /// <list type="bullet">
    ///   <item>The <c>DiffEngine_Tool</c> environment variable</item>
    ///   <item>The current IDE (Visual Studio, Visual Studio Code, Rider)</item>
    /// </list>
    /// </summary>
    /// <remarks>The <c>DiffEngine_Disabled</c> environment variable disable all diff tool even if set explicitly</remarks>
    public IEnumerable<MergeTool>? MergeTools { get; set; } = DefaultMergeTools;

    /// <summary>
    /// Before editing a file, use the PDB to validate the file path containing the snapshot.
    /// </summary>
    public bool ValidateSourceFilePathUsingPdbInfoWhenAvailable { get; set; } = true;

    /// <summary>
    /// Before editing a file, use the PDB to validate the line number containing the snapshot.
    /// </summary>
    /// <remarks>
    /// PDB and <see cref="System.Runtime.CompilerServices.CallerLineNumberAttribute"/> does not provide
    /// the same value. PDB provide the start of the expression whereas the attribute provide the line
    /// containing the call to the method. In the case of a multiline expression, the values can differ.
    /// </remarks>
    public bool ValidateLineNumberUsingPdbInfoWhenAvailable { get; set; }

    /// <summary>
    /// Update snapshots even when the snapshot is already valid.
    /// This can be use to reformat snapshots.
    /// </summary>
    public bool ForceUpdateSnapshots { get; set; }

    public InlineSnapshotSettings()
    {
        Scrubbers = [];
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Clone constructor (use by the with keyword)")]
    private InlineSnapshotSettings(InlineSnapshotSettings? options)
    {
        Scrubbers = [];
        if (options != null)
        {
            Indentation = options.Indentation;
            EndOfLine = options.EndOfLine;
            FileEncoding = options.FileEncoding;
            AutoDetectContinuousEnvironment = options.AutoDetectContinuousEnvironment;
            SnapshotUpdateStrategy = options.SnapshotUpdateStrategy;
            SnapshotSerializer = options.SnapshotSerializer;
            SnapshotComparer = options.SnapshotComparer;
            ErrorMessageFormatter = options.ErrorMessageFormatter;
            AssertionExceptionCreator = options.AssertionExceptionCreator;
            AllowedStringFormats = options.AllowedStringFormats;
            MergeTools = options.MergeTools is null ? null : [.. options.MergeTools];
            ValidateSourceFilePathUsingPdbInfoWhenAvailable = options.ValidateSourceFilePathUsingPdbInfoWhenAvailable;
            ValidateLineNumberUsingPdbInfoWhenAvailable = options.ValidateLineNumberUsingPdbInfoWhenAvailable;
            ForceUpdateSnapshots = options.ForceUpdateSnapshots;

            foreach (var item in options.Scrubbers)
            {
                Scrubbers.Add(item);
            }
        }
    }

    [DoesNotReturn]
    internal void AssertSnapshot(string? expected, string? actual)
    {
        var errorMessage = "Snapshots do not match:\n" + ErrorMessageFormatter.FormatMessage(expected, actual);
        throw AssertionExceptionCreator.CreateException(errorMessage);
    }
}
