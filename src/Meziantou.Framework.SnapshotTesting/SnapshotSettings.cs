using System.Collections.Immutable;
using System.Security.Cryptography;
using Meziantou.Framework.LLMContext;
using Meziantou.Framework.SnapshotTesting.MergeTools;

namespace Meziantou.Framework.SnapshotTesting;

public sealed record SnapshotSettings
{
    // The longest file name ext4, APFS and most other file systems accept, in UTF-8 bytes. NTFS counts UTF-16
    // code units instead, which never exceed the UTF-8 byte count.
    private const int MaxFileNameByteCount = 255;

    private static readonly ImmutableArray<MergeTool> DefaultMergeTools = ImmutableArray.Create(
        MergeTool.DiffToolFromEnvironmentVariable,
        MergeTool.GitMergeTool,
        MergeTool.GitDiffTool,
        MergeTool.VisualStudioMergeIfCurrentProcess,
        MergeTool.VisualStudioCodeIfCurrentProcess,
        MergeTool.RiderIfCurrentProcess,
        new AutoDiffEngineTool());


    internal const string AutoDetectContinuousEnvironmentVariableName = "SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT";

    public static SnapshotSettings Default { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether to automatically detect continuous integration, continuous testing, and LLM environments and disable snapshot updates.</summary>
    /// <remarks>
    /// The default value is <see langword="true" />, unless the <c>SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT</c>
    /// environment variable is set to <c>false</c>, <c>0</c>, <c>no</c> or <c>off</c>.
    /// </remarks>
    public bool AutoDetectContinuousEnvironment { get; set; } = ContinuousEnvironmentDetector.IsAutoDetectionEnabled(AutoDetectContinuousEnvironmentVariableName);

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

    public SnapshotNamingStrategy SnapshotNamingStrategy
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public SnapshotSerializerCollection Serializers { get; }

    /// <summary>
    /// Gets or sets the ordered list of tools used by <see cref="SnapshotUpdateStrategy.MergeTool" /> and
    /// <see cref="SnapshotUpdateStrategy.MergeToolSync" />. The first tool that can be started is used.
    /// The default list tries the <c>DiffEngine_Tool</c> environment variable, the git merge and diff tools, the current IDE
    /// (Visual Studio, Visual Studio Code, Rider), and then the GUI tools detected by DiffEngine. Terminal tools such as Vim
    /// are only used when named explicitly.
    /// If <see langword="null" /> or empty, no merge tool is launched and the assertion failure is reported.
    /// </summary>
    /// <remarks>
    /// The <c>DiffEngine_Disabled</c> environment variable disables all merge tools, even the ones set explicitly. Merge tools
    /// are also never launched on a continuous integration server, in a continuous testing runner, or in an LLM agent.
    /// </remarks>
    public IEnumerable<MergeTool>? MergeTools { get; set; }

    public SnapshotComparerCollection Comparers { get; }

    public IList<Scrubber> Scrubbers { get; }

    public SnapshotSettings()
    {
        Serializers =
        [
            HumanReadableSnapshotSerializer.DefaultInstance,
            ByteArraySnapshotSerializer.Instance,
            StreamSnapshotSerializer.Instance,
        ];
        Comparers = new SnapshotComparerCollection();
        Comparers.Set(SnapshotType.None, ByteArraySnapshotComparer.Instance);
        Comparers.Set(SnapshotType.Default, TextSnapshotComparer.Instance);
        Comparers.Set(SnapshotType.Svg, TextSnapshotComparer.Instance);
        Scrubbers = new CopyOnWriteList<Scrubber>();
        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Default;
        MaxSnapshotFileNameLength = 128;
        SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName;
        SnapshotPathStrategy = DefaultSnapshotPath;
        MergeTools = DefaultMergeTools;
    }

    private SnapshotSettings(SnapshotSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Serializers = new SnapshotSerializerCollection(options.Serializers);
        Comparers = new SnapshotComparerCollection(options.Comparers);
        Scrubbers = new CopyOnWriteList<Scrubber>(options.Scrubbers);

        AutoDetectContinuousEnvironment = options.AutoDetectContinuousEnvironment;
        ForceUpdateSnapshots = options.ForceUpdateSnapshots;
        SnapshotUpdateStrategy = options.SnapshotUpdateStrategy;
        MaxSnapshotFileNameLength = options.MaxSnapshotFileNameLength;
        SnapshotNamingStrategy = options.SnapshotNamingStrategy;
        SnapshotPathStrategy = options.SnapshotPathStrategy;
        MergeTools = options.MergeTools is null ? null : [.. options.MergeTools];
    }

    internal static bool IsRunningOnContinuousIntegration() => ContinuousEnvironmentDetector.GetDetectedEnvironmentDescription() is not null;

    /// <summary>
    /// Indicates whether the snapshot names come from the library: the default path strategy combined with a
    /// built-in naming strategy. Only those names are checked for collisions, as a custom strategy may give
    /// several tests the same file on purpose.
    /// </summary>
    internal static bool UsesBuiltInSnapshotNames(SnapshotSettings settings)
    {
        return settings.SnapshotPathStrategy == (SnapshotPathStrategy)DefaultSnapshotPath && IsBuiltInNamingStrategy(settings.SnapshotNamingStrategy);
    }

    private static bool IsBuiltInNamingStrategy(SnapshotNamingStrategy strategy)
    {
        return ReferenceEquals(strategy, SnapshotNamingStrategies.ClassName_TestName) ||
               ReferenceEquals(strategy, SnapshotNamingStrategies.TestName) ||
               ReferenceEquals(strategy, SnapshotNamingStrategies.FullName);
    }

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

        var rawStartPart = context.Settings.SnapshotNamingStrategy(context) ?? "";
        var startPart = RemoveSnapshotFileMarkers(SanitizeFragment(rawStartPart));

        // Sanitizing loses information - 'Case_a/b' and 'Case_a?b' both become 'Case_a_b' - so a name it
        // changed only stays distinct once the hash of the original name is part of the file name.
        var isNameSanitized = !string.Equals(startPart, rawStartPart, StringComparison.Ordinal);
        if (startPart.Length == 0)
        {
            startPart = "snapshot";
        }

        // The '_<index>' suffix tells apart the files of an assertion that produces several snapshots, and the
        // '~<ordinal>' suffix tells apart the assertions of a test that would otherwise share a name. '~' is never
        // produced by the sanitization, so the second assertion of a test cannot take the name of another test.
        // The naming of a custom strategy is left alone: it may already tell the assertions apart.
        var callOrdinal = IsBuiltInNamingStrategy(context.Settings.SnapshotNamingStrategy) ? context.CallOrdinal : 1;
        var ordinalPart = callOrdinal > 1 ? "~" + callOrdinal.ToString(CultureInfo.InvariantCulture) : "";
        var indexPart = context.SnapshotCount > 1 ? "_" + context.Index.ToString(CultureInfo.InvariantCulture) : "";
        var extensionPart = ".verified." + extension;
        var suffixWithoutHash = ordinalPart + indexPart + extensionPart;
        var shouldAddHashSuffix =
            isNameSanitized ||
            startPart.Length > context.Settings.MaxSnapshotFileNameLength - suffixWithoutHash.Length ||
            Encoding.UTF8.GetByteCount(startPart) + Encoding.UTF8.GetByteCount(suffixWithoutHash) > MaxFileNameByteCount ||
            IsReservedSnapshotName(startPart) ||
            IsWindowsReservedDeviceName(startPart + ordinalPart + indexPart);

        var suffix = suffixWithoutHash;
        if (shouldAddHashSuffix)
        {
            // The line number is deliberately not part of the hash: it would rename the snapshot whenever
            // anything above the assertion moves. Two assertions of one test are told apart by the ordinal
            // suffix instead.
            // The source file name is used rather than its full path: the snapshot is stored next to the
            // source file, so the name is enough to tell two files of that directory apart, while the
            // absolute path would tie the file name to where the repository happens to be checked out.
            var hashInput = $"{context.SourceFilePath.Name}|{context.MethodName}|{context.ClassName}|{context.Type.Type}|{rawStartPart}|{context.TestContext?.TestName}|{FormatMetadata(context.TestContext?.Metadata)}";
            var hash = ToHexSha256(hashInput, length: 8);
            suffix = "_" + hash + ordinalPart + indexPart + extensionPart;
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

        startPart = TruncateToByteCount(startPart, MaxFileNameByteCount - Encoding.UTF8.GetByteCount(suffix));
        return startPart + suffix;
    }

    /// <summary>
    /// Shortens a name so its UTF-8 encoding fits in the given number of bytes, without splitting a character.
    /// </summary>
    private static string TruncateToByteCount(string value, int maxByteCount)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxByteCount)
            return value;

        var byteCount = 0;
        var length = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            byteCount += rune.Utf8SequenceLength;
            if (byteCount > maxByteCount)
                break;

            length += rune.Utf16SequenceLength;
        }

        return length == 0 ? "s" : value[..length];
    }

    private static bool IsReservedSnapshotName(string value)
    {
        return value.EndsWith(".verified", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".actual", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the dot that starts a <c>.verified.</c> or <c>.actual.</c> sequence inside a name with <c>_</c>. With a
    /// marker in its name - a test called <c>Parse.actual.value</c> - a verified file looks like an actual file to the
    /// approval tool, including the versions of the tool that are already installed. The case is ignored, as the tool's
    /// file pattern matches the marker in any case on Windows and macOS.
    /// </summary>
    private static string RemoveSnapshotFileMarkers(string value)
    {
        while (true)
        {
            var index = value.IndexOf(SnapshotFileName.VerifiedMarker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                index = value.IndexOf(SnapshotFileName.ActualMarker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return value;
            }

            value = string.Concat(value.AsSpan(0, index), "_", value.AsSpan(index + 1));
        }
    }

    /// <summary>
    /// Indicates whether Windows opens a device rather than the file: on the Windows versions that map a device name
    /// followed by an extension, <c>NUL.verified.txt</c> is the <c>NUL</c> device.
    /// </summary>
    private static bool IsWindowsReservedDeviceName(string fileName)
    {
        var dotIndex = fileName.IndexOf('.', StringComparison.Ordinal);
        var name = dotIndex < 0 ? fileName.AsSpan() : fileName.AsSpan(0, dotIndex);
        return name.Length switch
        {
            3 => name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals("NUL", StringComparison.OrdinalIgnoreCase),
            4 => (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 name[3] is (>= '0' and <= '9') or '\u00B9' or '\u00B2' or '\u00B3',
            _ => false,
        };
    }

    internal static string FormatMetadata(IReadOnlyDictionary<string, string?>? metadata)
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
