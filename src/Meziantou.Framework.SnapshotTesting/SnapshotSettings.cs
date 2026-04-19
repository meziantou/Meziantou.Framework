using System.Collections.Immutable;
using System.Security.Cryptography;
using Meziantou.Framework.SnapshotTesting.MergeTools;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotSettings
{
    private static readonly ImmutableArray<MergeTool> DefaultMergeTools = ImmutableArray.Create(
        MergeTool.DiffToolFromEnvironmentVariable,
        MergeTool.GitMergeTool,
        MergeTool.GitDiffTool,
        MergeTool.VisualStudioMergeIfCurrentProcess,
        MergeTool.VisualStudioCodeIfCurrentProcess,
        MergeTool.RiderIfCurrentProcess,
        new AutoDiffEngineTool());

    private readonly List<ISnapshotSerializer> _serializers;
    private readonly Dictionary<SnapshotType, ISnapshotComparer> _comparers;

    public static SnapshotSettings Default { get; set; } = new();

    public bool AutoDetectContinuousEnvironment { get; set; } = true;

    public bool ForceUpdateSnapshots { get; set; }

    public SnapshotUpdateStrategy SnapshotUpdateStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public AssertionExceptionBuilder AssertionExceptionCreator
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public AssertionMessageFormatter ErrorMessageFormatter
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public int MaxSnapshotFileNameLength
    {
        get;
        set
        {
            if (value < 20)
                throw new ArgumentOutOfRangeException(nameof(value), "The value must be at least 20.");

            field = value;
        }
    }

    public SnapshotPathStrategy SnapshotPathStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public IList<ISnapshotSerializer> Serializers { get; } = [];

    /// <summary>
    /// Set the ordered list of tools to diff snapshots.
    /// If null or empty, the diff tool is determined by
    /// <list type="bullet">
    ///   <item>The <c>DiffEngine_Tool</c> environment variable</item>
    ///   <item>The current IDE (Visual Studio, Visual Studio Code, Rider)</item>
    /// </list>
    /// </summary>
    /// <remarks>The <c>DiffEngine_Disabled</c> environment variable disable all diff tool even if set explicitly</remarks>
    public IEnumerable<MergeTool>? MergeTools { get; set; }

    public IReadOnlyDictionary<SnapshotType, ISnapshotComparer> Comparers => _comparers;

    public SnapshotSettings()
    {
        _serializers = new List<ISnapshotSerializer>() { HumanReadableSnapshotSerializer.DefaultInstance, ByteArraySnapshotSerializer.Instance, StreamSnapshotSerializer.Instance };
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>() { [SnapshotType.None] = ByteArraySnapshotComparer.Instance };
        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Default;
        AssertionExceptionCreator = AssertionExceptionBuilder.Default;
        ErrorMessageFormatter = InlineDiffAssertionMessageFormatter.Instance;
        MaxSnapshotFileNameLength = 128;
        SnapshotPathStrategy = DefaultSnapshotPath;
        MergeTools = DefaultMergeTools;
    }

    private SnapshotSettings(SnapshotSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _serializers = [.. options._serializers];
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>(options._comparers);
        AutoDetectContinuousEnvironment = options.AutoDetectContinuousEnvironment;
        ForceUpdateSnapshots = options.ForceUpdateSnapshots;
        SnapshotUpdateStrategy = options.SnapshotUpdateStrategy;
        AssertionExceptionCreator = options.AssertionExceptionCreator;
        ErrorMessageFormatter = options.ErrorMessageFormatter;
        MaxSnapshotFileNameLength = options.MaxSnapshotFileNameLength;
        SnapshotPathStrategy = options.SnapshotPathStrategy;
        MergeTools = options.MergeTools is null ? null : [.. options.MergeTools];
    }

    public void SetSnapshotComparer(SnapshotType type, ISnapshotComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _comparers[type] = comparer;
    }

    public ISnapshotComparer GetSnapshotComparer(SnapshotType type)
    {
        if (_comparers.TryGetValue(type, out var comparer))
            return comparer;

        if (_comparers.TryGetValue(SnapshotType.None, out var defaultComparer))
            return defaultComparer;

        return ByteArraySnapshotComparer.Instance;
    }

    internal static bool IsRunningOnContinuousIntegration() => BuildServerDetector.Detected || ContinuousTestingDetector.Detected;

    private static FullPath DefaultSnapshotPath(SnapshotPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SourceFilePath.Parent / "__snapshots__" / BuildSnapshotFileName(context);
    }

    private static string BuildSnapshotFileName(SnapshotPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var extension = context.Extension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = "bin";
        }
        else
        {
            extension = extension.TrimStart('.');
            extension = SanitizeFragment(extension);
            if (extension.Length == 0)
            {
                extension = "bin";
            }
        }

        var startPart = context.TestContext?.TestName ?? context.MethodName ?? context.MemberName ?? "";
        startPart = SanitizeFragment(startPart);
        if (startPart.Length == 0)
        {
            startPart = "snapshot";
        }

        var hasMultipleSnapshots = context.SnapshotCount > 1;
        var indexPart = context.Index.ToString(CultureInfo.InvariantCulture);
        var suffixWithoutHash = hasMultipleSnapshots ? "_" + indexPart + ".verified." + extension : ".verified." + extension;
        var shouldAddHashSuffix =
            startPart.Length > context.Settings.MaxSnapshotFileNameLength - suffixWithoutHash.Length ||
            IsReservedSnapshotName(startPart);

        var suffix = suffixWithoutHash;
        if (shouldAddHashSuffix)
        {
            var hashInput = $"{context.SourceFilePath}|{context.MethodName}|{context.MemberName}|{context.LineNumber}|{context.Type.Type}|{context.TestContext?.TestName}|{FormatMetadata(context.TestContext?.Metadata)}";
            var hash = ToHexSha256(hashInput, length: 8);
            suffix = hasMultipleSnapshots ? "_" + hash + "_" + indexPart + ".verified." + extension : "_" + hash + ".verified." + extension;
        }

        var maxStartLength = context.Settings.MaxSnapshotFileNameLength - suffix.Length;
        if (maxStartLength <= 0)
        {
            startPart = "s";
        }
        else if (startPart.Length > maxStartLength)
        {
            startPart = startPart[..maxStartLength];
        }

        return startPart + suffix;
    }

    private static bool IsReservedSnapshotName(string value)
    {
        return value.EndsWith(".verified", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".actual", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return "";

        return string.Join('|', metadata.OrderBy(static entry => entry.Key, StringComparer.Ordinal).Select(static entry => $"{entry.Key}={entry.Value}"));
    }

    private static string ToHexSha256(string value, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var hex = Convert.ToHexStringLower(hash);
        if (length >= hex.Length)
            return hex;

        return hex[..length];
    }

    private static string SanitizeFragment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrEmpty(value))
            return "";

        var buffer = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                buffer.Append(c);
            }
            else if (buffer.Length == 0 || buffer[^1] != '_')
            {
                buffer.Append('_');
            }
        }

        return buffer.ToString().Trim('_');
    }
}
