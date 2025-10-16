using System.Collections.Immutable;
using DiffEngine;
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

    public static InlineSnapshotSettings Default { get; set; } = new();

    public string? Indentation { get; set; }
    public string? EndOfLine { get; set; }
    public Encoding? FileEncoding { get; set; }

    public bool AutoDetectContinuousEnvironment { get; set; } = true;
    public CSharpStringFormats AllowedStringFormats { get; set; } = CSharpStringFormats.Default;

    public SnapshotUpdateStrategy SnapshotUpdateStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = SnapshotUpdateStrategy.Default;

    public SnapshotSerializer SnapshotSerializer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = HumanReadableSnapshotSerializer.DefaultInstance;

    public SnapshotComparer SnapshotComparer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = SnapshotComparer.Default;

    public AssertionMessageFormatter ErrorMessageFormatter
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = InlineDiffAssertionMessageFormatter.Instance;

    public AssertionExceptionBuilder AssertionExceptionCreator
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = AssertionExceptionBuilder.Default;

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

    public override string ToString() => $"""
        Indentation = {Indentation},
        EndOfLine = {EndOfLine},
        FileEncoding = {FileEncoding},
        AutoDetectContinuousEnvironment = {AutoDetectContinuousEnvironment},
        SnapshotUpdateStrategy = {SnapshotUpdateStrategy},
        SnapshotSerializer = {SnapshotSerializer},
        SnapshotComparer = {SnapshotComparer},
        ErrorMessageFormatter = {ErrorMessageFormatter},
        AssertionExceptionCreator = {AssertionExceptionCreator},
        AllowedStringFormats = {AllowedStringFormats},
        MergeTools = {MergeTools},
        ValidateSourceFilePathUsingPdbInfoWhenAvailable = {ValidateSourceFilePathUsingPdbInfoWhenAvailable},
        ValidateLineNumberUsingPdbInfoWhenAvailable = {ValidateLineNumberUsingPdbInfoWhenAvailable},
        ForceUpdateSnapshots = {ForceUpdateSnapshots},
        Scrubbers = {Scrubbers}
        IsRunningOnContinuousIntegration = {IsRunningOnContinuousIntegration()}
        BuildServerDetector = {BuildServerDetector.Detected}
        ContinuousTestingDetector = {ContinuousTestingDetector.Detected}
        """;

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

    internal static bool IsRunningOnContinuousIntegration() => BuildServerDetector.Detected || ContinuousTestingDetector.Detected;

    [DoesNotReturn]
    internal void AssertSnapshot(string? expected, string? actual)
    {
        var errorMessage = "Snapshots do not match:\n" + ErrorMessageFormatter.FormatMessage(expected, actual);
        throw AssertionExceptionCreator.CreateException(errorMessage);
    }
}
