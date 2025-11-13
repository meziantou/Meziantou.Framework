using System.Collections.Immutable;
using DiffEngine;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Provides configuration settings for inline snapshot testing.</summary>
public sealed record InlineSnapshotSettings
{
    private static readonly ImmutableArray<MergeTool> DefaultMergeTools = ImmutableArray.Create(
        MergeTool.DiffToolFromEnvironmentVariable,
        MergeTool.GitMergeTool,
        MergeTool.GitDiffTool,
        MergeTool.VisualStudioMergeIfCurrentProcess,
        MergeTool.VisualStudioCodeIfCurrentProcess,
        MergeTool.RiderIfCurrentProcess,
        new AutoDiffEngineTool());

    /// <summary>Gets or sets the default settings used for snapshot validation.</summary>
    public static InlineSnapshotSettings Default { get; set; } = new();

    /// <summary>Gets or sets the indentation string to use when writing snapshots. If null, the indentation is detected from the PDB file.</summary>
    public string? Indentation { get; set; }

    /// <summary>Gets or sets the end-of-line string to use when writing snapshots. If null, the end-of-line is detected from the source file.</summary>
    public string? EndOfLine { get; set; }

    /// <summary>Gets or sets the file encoding to use when writing snapshots. If null, the encoding is detected from the source file.</summary>
    public Encoding? FileEncoding { get; set; }

    /// <summary>Gets or sets a value indicating whether to automatically detect CI environments and disable snapshot updates.</summary>
    public bool AutoDetectContinuousEnvironment { get; set; } = true;

    /// <summary>Gets or sets the allowed C# string formats for writing snapshots (quoted, verbatim, raw, etc.).</summary>
    public CSharpStringFormats AllowedStringFormats { get; set; } = CSharpStringFormats.Default;

    /// <summary>Gets or sets the strategy for updating snapshots when they don't match.</summary>
    public SnapshotUpdateStrategy SnapshotUpdateStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = SnapshotUpdateStrategy.Default;

    /// <summary>Gets or sets the serializer used to convert objects to snapshot strings.</summary>
    public SnapshotSerializer SnapshotSerializer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = HumanReadableSnapshotSerializer.DefaultInstance;

    /// <summary>Gets or sets the comparer used to determine if two snapshots are equal.</summary>
    public SnapshotComparer SnapshotComparer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = SnapshotComparer.Default;

    /// <summary>Gets or sets the formatter used to create error messages when snapshots don't match.</summary>
    public AssertionMessageFormatter ErrorMessageFormatter
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = InlineDiffAssertionMessageFormatter.Instance;

    /// <summary>Gets or sets the builder used to create assertion exceptions when snapshots don't match.</summary>
    public AssertionExceptionBuilder AssertionExceptionCreator
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    } = AssertionExceptionBuilder.Default;

    /// <summary>Gets the list of scrubbers applied to snapshots after serialization.</summary>
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

    /// <summary>Before editing a file, use the PDB to validate the file path containing the snapshot.</summary>
    public bool ValidateSourceFilePathUsingPdbInfoWhenAvailable { get; set; } = true;

    /// <summary>Before editing a file, use the PDB to validate the line number containing the snapshot.</summary>
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
