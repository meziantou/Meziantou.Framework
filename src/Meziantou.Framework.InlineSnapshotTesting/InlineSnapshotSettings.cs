using System.Collections.Immutable;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;
using Meziantou.Framework.LLMContext;

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

    /// <summary>Gets or sets the indentation string to use when writing snapshots. If null, the indentation is detected from the source file.</summary>
    public string? Indentation { get; set; }

    /// <summary>Gets or sets the end-of-line string to use when writing snapshots. If null, the end-of-line is detected from the source file.</summary>
    public string? EndOfLine { get; set; }

    /// <summary>Gets or sets the file encoding to use when writing snapshots. If null, the encoding is detected from the source file.</summary>
    public Encoding? FileEncoding { get; set; }

    internal const string AutoDetectContinuousEnvironmentVariableName = "INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT";

    /// <summary>Gets or sets a value indicating whether to automatically detect continuous integration, continuous testing, and LLM environments and disable snapshot updates.</summary>
    /// <remarks>
    /// The default value is <see langword="true" />, unless the <c>INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT</c>
    /// environment variable is set to <c>false</c>, <c>0</c>, <c>no</c> or <c>off</c>.
    /// </remarks>
    public bool AutoDetectContinuousEnvironment { get; set; } = ContinuousEnvironmentDetector.IsAutoDetectionEnabled(AutoDetectContinuousEnvironmentVariableName);

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

    /// <summary>Gets the list of scrubbers applied to snapshots after serialization.</summary>
    public IList<Scrubber> Scrubbers { get; }

    /// <summary>
    /// Gets or sets the ordered list of tools used by <see cref="SnapshotUpdateStrategy.MergeTool" /> and
    /// <see cref="SnapshotUpdateStrategy.MergeToolSync" />. The first tool that can be started is used.
    /// The default list tries the <c>DiffEngine_Tool</c> environment variable, the git merge and diff tools, the current IDE
    /// (Visual Studio, Visual Studio Code, Rider), and then the GUI tools detected by DiffEngine. Terminal tools such as Vim
    /// are only used when they are named explicitly.
    /// If <see langword="null" /> or empty, no merge tool is launched and the assertion failure is reported.
    /// </summary>
    /// <remarks>
    /// The <c>DiffEngine_Disabled</c> environment variable disables all merge tools, even the ones set explicitly. Merge tools
    /// are also never launched on a continuous integration server, in a continuous testing runner, or in an LLM agent.
    /// </remarks>
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
        AllowedStringFormats = {AllowedStringFormats},
        MergeTools = {(MergeTools is null ? "" : string.Join(", ", MergeTools))},
        ValidateSourceFilePathUsingPdbInfoWhenAvailable = {ValidateSourceFilePathUsingPdbInfoWhenAvailable},
        ValidateLineNumberUsingPdbInfoWhenAvailable = {ValidateLineNumberUsingPdbInfoWhenAvailable},
        ForceUpdateSnapshots = {ForceUpdateSnapshots},
        Scrubbers = {string.Join(", ", Scrubbers)},
        IsRunningOnContinuousIntegration = {IsRunningOnContinuousIntegration()},
        BuildServerDetector = {BuildServerDetector.Detected},
        ContinuousTestingDetector = {ContinuousTestingDetector.Detected},
        LLMContextDetector = {LLMEnvironmentDetector.Detected}
        """;

    public InlineSnapshotSettings()
    {
        Scrubbers = [];
    }

    private InlineSnapshotSettings(InlineSnapshotSettings options)
    {
        Scrubbers = [];
        AllowedStringFormats = options.AllowedStringFormats;
        AutoDetectContinuousEnvironment = options.AutoDetectContinuousEnvironment;
        EndOfLine = options.EndOfLine;
        ErrorMessageFormatter = options.ErrorMessageFormatter;
        FileEncoding = options.FileEncoding;
        ForceUpdateSnapshots = options.ForceUpdateSnapshots;
        Indentation = options.Indentation;
        MergeTools = options.MergeTools is null ? null : [.. options.MergeTools];
        SnapshotComparer = options.SnapshotComparer;
        SnapshotSerializer = options.SnapshotSerializer;
        SnapshotUpdateStrategy = options.SnapshotUpdateStrategy;
        ValidateLineNumberUsingPdbInfoWhenAvailable = options.ValidateLineNumberUsingPdbInfoWhenAvailable;
        ValidateSourceFilePathUsingPdbInfoWhenAvailable = options.ValidateSourceFilePathUsingPdbInfoWhenAvailable;

        foreach (var item in options.Scrubbers)
        {
            Scrubbers.Add(item);
        }
    }

    internal static bool IsRunningOnContinuousIntegration() => ContinuousEnvironmentDetector.GetDetectedEnvironmentDescription() is not null;

    [DoesNotReturn]
    internal void AssertSnapshot(string? expected, string? actual)
    {
        var errorMessage =
            "Snapshots do not match:\n" +
            ErrorMessageFormatter.FormatMessage(expected, actual) +
            "\n\n" +
            GetResolutionGuidanceMessage();

        // Without this, the guidance above suggests a strategy that the environment detection silently ignores.
        if (AutoDetectContinuousEnvironment && ContinuousEnvironmentDetector.GetDetectedEnvironmentDescription() is { } environment)
        {
            errorMessage += "\n\n" + ContinuousEnvironmentDetector.FormatUpdatesDisabledMessage(environment, AutoDetectContinuousEnvironmentVariableName, nameof(InlineSnapshotSettings));
        }

        throw new InlineSnapshotAssertionException(errorMessage);
    }

    private static string GetResolutionGuidanceMessage() =>
        """
        Resolution guidance:
          - Compare the expected snapshot and received value shown above.
          - If the new behavior is correct, update the inline snapshot in source code:
            - remove lines starting with '-' from the snapshot
            - add lines starting with '+' to the snapshot
          - To update snapshots automatically, re-run the test with INLINESNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure).
          - If the old behavior is correct, fix the test or production code so the output matches the snapshot.
          - Re-run the test.
        """;
}
