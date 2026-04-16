using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework;
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

    private readonly Dictionary<SnapshotType, ISnapshotSerializer> _serializers;
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

    public SnapshotFileNameStrategy FileNameStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public SnapshotPathStrategy PathStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public ISnapshotSerializer DefaultSerializer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public ISnapshotComparer DefaultComparer
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

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

    public IReadOnlyDictionary<SnapshotType, ISnapshotSerializer> Serializers => _serializers;

    public IReadOnlyDictionary<SnapshotType, ISnapshotComparer> Comparers => _comparers;

    public SnapshotSettings()
    {
        _serializers = new Dictionary<SnapshotType, ISnapshotSerializer>();
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>();
        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Default;
        AssertionExceptionCreator = AssertionExceptionBuilder.Default;
        ErrorMessageFormatter = InlineDiffAssertionMessageFormatter.Instance;
        DefaultSerializer = HumanReadableSnapshotSerializer.DefaultInstance;
        DefaultComparer = ByteArraySnapshotComparer.Default;
        MaxSnapshotFileNameLength = 128;
        FileNameStrategy = DefaultFileName;
        PathStrategy = DefaultPath;
        MergeTools = DefaultMergeTools;
    }

    private SnapshotSettings(SnapshotSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _serializers = new Dictionary<SnapshotType, ISnapshotSerializer>(options._serializers);
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>(options._comparers);
        AutoDetectContinuousEnvironment = options.AutoDetectContinuousEnvironment;
        ForceUpdateSnapshots = options.ForceUpdateSnapshots;
        SnapshotUpdateStrategy = options.SnapshotUpdateStrategy;
        AssertionExceptionCreator = options.AssertionExceptionCreator;
        ErrorMessageFormatter = options.ErrorMessageFormatter;
        MaxSnapshotFileNameLength = options.MaxSnapshotFileNameLength;
        FileNameStrategy = options.FileNameStrategy;
        PathStrategy = options.PathStrategy;
        DefaultSerializer = options.DefaultSerializer;
        DefaultComparer = options.DefaultComparer;
        MergeTools = options.MergeTools is null ? null : [.. options.MergeTools];
    }

    public void SetSnapshotSerializer(SnapshotType type, ISnapshotSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializers[type] = serializer;
    }

    public void SetSnapshotComparer(SnapshotType type, ISnapshotComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _comparers[type] = comparer;
    }

    public ISnapshotSerializer GetSnapshotSerializer(SnapshotType type)
    {
        if (_serializers.TryGetValue(type, out var serializer))
            return serializer;

        return DefaultSerializer;
    }

    public ISnapshotComparer GetSnapshotComparer(SnapshotType type)
    {
        if (_comparers.TryGetValue(type, out var comparer))
            return comparer;

        return DefaultComparer;
    }

    internal static bool IsRunningOnContinuousIntegration() => BuildServerDetector.Detected || ContinuousTestingDetector.Detected;

    private static string DefaultFileName(SnapshotFileNameContext context)
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

        var startPart = context.TestContext?.TestName ?? context.MethodName ?? context.MemberName ?? "snapshot";
        startPart = SanitizeFragment(startPart);
        if (startPart.Length == 0)
        {
            startPart = "snapshot";
        }

        var hashInput = $"{context.SourceFilePath}|{context.MethodName}|{context.MemberName}|{context.LineNumber}|{context.Type.Type}|{context.TestContext?.TestName}|{context.TestContext?.Parameters}";
        var hash = ToHexSha256(hashInput, length: 10);
        var indexPart = context.Index.ToString(CultureInfo.InvariantCulture);
        var suffix = "_" + hash + "_" + indexPart + "." + extension;
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

    private static FullPath DefaultPath(SnapshotPathContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SourceFilePath.Parent / "__snapshots__" / context.FileName;
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
