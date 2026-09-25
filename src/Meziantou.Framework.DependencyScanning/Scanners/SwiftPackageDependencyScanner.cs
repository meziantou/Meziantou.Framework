using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Swift Package Manager files (Package.resolved, Package.swift and Package@swift-X.Y.swift) for dependencies.</summary>
public sealed class SwiftPackageDependencyScanner : DependencyScanner
{
    private const string PackageResolvedFileName = "Package.resolved";
    private const string PackageSwiftFileName = "Package.swift";
    private const string VersionSpecificPackageSwiftFileNamePrefix = "Package@swift-";
    private const string SwiftFileExtension = ".swift";

    private static readonly string[] SourceLabels = ["name", "id", "url", "location", "path"];
    private static readonly string[] RequirementLabels = ["from", "exact", "branch", "revision"];
    private static readonly string[] RequirementFactoryMethods = ["upToNextMajor", "upToNextMinor", "exact", "branch", "revision"];
    private static readonly string[] RequirementTypeQualifiers = ["Package", "Dependency", "Requirement"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.SwiftPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName(PackageResolvedFileName, ignoreCase: false) ||
               IsPackageManifestFileName(context.FileName);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var fileName = Path.GetFileName(context.FullPath);
        if (string.Equals(fileName, PackageResolvedFileName, StringComparison.Ordinal))
        {
            await ScanPackageResolvedAsync(context).ConfigureAwait(false);
            return;
        }

        if (IsPackageManifestFileName(fileName))
        {
            await ScanPackageSwiftAsync(context).ConfigureAwait(false);
        }
    }

    private static bool IsPackageManifestFileName(ReadOnlySpan<char> fileName)
    {
        if (fileName.Equals(PackageSwiftFileName, StringComparison.Ordinal))
            return true;

        // Version-specific manifest, such as Package@swift-5.9.swift
        if (!fileName.StartsWith(VersionSpecificPackageSwiftFileNamePrefix, StringComparison.Ordinal) || !fileName.EndsWith(SwiftFileExtension, StringComparison.Ordinal))
            return false;

        var version = fileName[VersionSpecificPackageSwiftFileNamePrefix.Length..^SwiftFileExtension.Length];
        if (version.IsEmpty || !char.IsAsciiDigit(version[0]) || !char.IsAsciiDigit(version[^1]))
            return false;

        foreach (var c in version)
        {
            if (!char.IsAsciiDigit(c) && c != '.')
                return false;
        }

        return true;
    }

    private async ValueTask ScanPackageResolvedAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            foreach (var pin in EnumeratePins(root))
            {
                var dependencyName = GetDependencyName(pin, out var dependencyNamePath);
                var dependencyVersion = GetDependencyVersion(pin, out var dependencyVersionPath, out var isVersionUpdatable);
                if (dependencyName is null && dependencyVersion is null)
                    continue;

                Location? versionLocation = null;
                if (dependencyVersionPath is not null)
                {
                    versionLocation = isVersionUpdatable ? new JsonLocation(context, dependencyVersionPath) : new NonUpdatableLocation(context);
                }

                context.ReportDependency(this, dependencyName, dependencyVersion, DependencyType.SwiftPackage,
                    nameLocation: dependencyNamePath is null ? null : new JsonLocation(context, dependencyNamePath),
                    versionLocation: versionLocation);
            }
        }
        catch (JsonException)
        {
        }
    }

    private async ValueTask ScanPackageSwiftAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        var tokens = SwiftLexer.Tokenize(text);
        var matchingParentheses = ComputeMatchingParentheses(text, tokens);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!IsPackageCall(text, tokens, i))
                continue;

            var openParenthesisIndex = i + 2;
            var closeParenthesisIndex = matchingParentheses[openParenthesisIndex];
            if (closeParenthesisIndex < 0)
                continue;

            var segments = SplitTopLevelArguments(text, tokens, openParenthesisIndex + 1, closeParenthesisIndex);

            var dependencyName = GetDependencyName(text, tokens, segments, out var dependencyNameRange);
            var dependencyVersion = GetDependencyVersion(text, tokens, segments, out var dependencyVersionRange);
            if (dependencyName is not null || dependencyVersion is not null)
            {
                context.ReportDependency(this, dependencyName, dependencyVersion, DependencyType.SwiftPackage,
                    nameLocation: dependencyNameRange is { } nameRange ? CreateLocation(context, text, nameRange) : null,
                    versionLocation: dependencyVersionRange is { } versionRange ? CreateLocation(context, text, versionRange) : null);
            }

            // Nested .package calls are part of the arguments of this call
            i = closeParenthesisIndex;
        }
    }

    private static bool IsPackageCall(string text, List<SwiftToken> tokens, int index)
    {
        if (index + 2 >= tokens.Count ||
            !tokens[index].Is(text, SwiftTokenKind.Punctuation, ".") ||
            !tokens[index + 1].Is(text, SwiftTokenKind.Identifier, "package") ||
            !tokens[index + 2].Is(text, SwiftTokenKind.Punctuation, "("))
        {
            return false;
        }

        // `foo.package(` is a member of something else, but `Package.Dependency.package(` is the fully qualified factory method
        if (index > 0)
        {
            var previous = tokens[index - 1];
            if (previous.Kind is SwiftTokenKind.Identifier && previous.End == tokens[index].Start)
                return previous.Is(text, SwiftTokenKind.Identifier, "Dependency");
        }

        return true;
    }

    private static int[] ComputeMatchingParentheses(string text, List<SwiftToken> tokens)
    {
        var result = new int[tokens.Count];
        var openParentheses = new Stack<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            result[i] = -1;
            var token = tokens[i];
            if (token.Is(text, SwiftTokenKind.Punctuation, "("))
            {
                openParentheses.Push(i);
            }
            else if (token.Is(text, SwiftTokenKind.Punctuation, ")") && openParentheses.TryPop(out var openIndex))
            {
                result[openIndex] = i;
            }
        }

        return result;
    }

    private static Location CreateLocation(ScanFileContext context, string text, TextRange range)
    {
        if (!range.IsVerbatim || range.Length <= 0 || ContainsNewLine(text, range))
            return new NonUpdatableLocation(context);

        return TextLocation.FromIndex(context.FileSystem, context.FullPath, text, range.Start, range.Length);
    }

    private static bool ContainsNewLine(string text, TextRange range)
    {
        for (var i = range.Start; i < range.End; i++)
        {
            if (text[i] is '\r' or '\n')
                return true;
        }

        return false;
    }

    private static IEnumerable<JsonObject> EnumeratePins(JsonObject root)
    {
        if (JsonNodeDocument.TryGetArray(root, "pins", out var pinsV2))
        {
            foreach (var pin in JsonNodeDocument.GetArray(pinsV2).OfType<JsonObject>())
            {
                yield return pin;
            }
        }

        if (JsonNodeDocument.TryGetObject(root, "object", out var rootObject) &&
            JsonNodeDocument.TryGetArray(rootObject, "pins", out var pinsV1))
        {
            foreach (var pin in JsonNodeDocument.GetArray(pinsV1).OfType<JsonObject>())
            {
                yield return pin;
            }
        }
    }

    private static string? GetDependencyName(JsonObject pin, out string? dependencyNamePath)
    {
        if (TryGetString(pin, "identity", out var dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "package", out dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "location", out dependencyName, out dependencyNamePath))
            return dependencyName;

        if (TryGetString(pin, "repositoryURL", out dependencyName, out dependencyNamePath))
            return dependencyName;

        dependencyNamePath = null;
        return null;
    }

    private static string? GetDependencyVersion(JsonObject pin, out string? dependencyVersionPath, out bool isUpdatable)
    {
        dependencyVersionPath = null;
        isUpdatable = false;

        if (!JsonNodeDocument.TryGetObject(pin, "state", out var state))
            return null;

        // SwiftPM checks out the pinned revision, so rewriting the version or the branch alone would leave the pin
        // pointing at the old commit. Only a revision-only pin can be updated in place.
        if (TryGetString(state, "version", out var version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "branch", out version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "revision", out version, out dependencyVersionPath))
        {
            isUpdatable = true;
            return version;
        }

        return null;
    }

    private static bool TryGetString(JsonObject jsonObject, string propertyName, out string? value, out string? valuePath)
    {
        if (JsonNodeDocument.TryGetProperty(jsonObject, propertyName, out var node) &&
            node is not null &&
            JsonNodeDocument.TryGetString(node, out var stringValue))
        {
            value = stringValue;
            valuePath = JsonNodeDocument.GetPath(node);
            return true;
        }

        value = null;
        valuePath = null;
        return false;
    }

    private static List<TokenRange> SplitTopLevelArguments(string text, List<SwiftToken> tokens, int start, int end)
    {
        var result = new List<TokenRange>();
        var depth = 0;
        var segmentStart = start;

        for (var i = start; i < end; i++)
        {
            var token = tokens[i];
            if (token.Kind is not SwiftTokenKind.Punctuation)
                continue;

            switch (text[token.Start])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;

                case ')' or ']' or '}':
                    if (depth > 0)
                    {
                        depth--;
                    }

                    break;

                case ',' when depth == 0:
                    result.Add(new TokenRange(segmentStart, i));
                    segmentStart = i + 1;
                    break;
            }
        }

        result.Add(new TokenRange(segmentStart, end));
        return result;
    }

    private static string? GetDependencyName(string text, List<SwiftToken> tokens, List<TokenRange> segments, out TextRange? dependencyNameRange)
    {
        foreach (var label in SourceLabels)
        {
            foreach (var segment in segments)
            {
                if (!TryGetLabel(text, tokens, segment, out var segmentLabel) || segmentLabel != label || segment.Length <= 2)
                    continue;

                if (TryGetStringLiteral(text, tokens, segment.Start + 2, segment.End, out var value, out var valueRange))
                {
                    dependencyNameRange = valueRange;
                    return value;
                }

                // An expression, such as a variable or a concatenation, is reported as written, and cannot be updated
                var expressionRange = new TextRange(tokens[segment.Start + 2].Start, tokens[segment.End - 1].End, IsVerbatim: false);
                dependencyNameRange = expressionRange;
                return text[expressionRange.Start..expressionRange.End];
            }
        }

        if (segments.Count > 0 && TryGetStringLiteral(text, tokens, segments[0].Start, segments[0].End, out var unlabeledValue, out var unlabeledValueRange))
        {
            dependencyNameRange = unlabeledValueRange;
            return unlabeledValue;
        }

        dependencyNameRange = null;
        return null;
    }

    private static string? GetDependencyVersion(string text, List<SwiftToken> tokens, List<TokenRange> segments, out TextRange? dependencyVersionRange)
    {
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || !IsRequirementArgument(text, tokens, segment))
                continue;

            // The whole requirement (e.g. `from: "1.0.0"`) is the version, so the kind of requirement can be changed on update.
            // The range starts at the first token and ends at the last one, so surrounding comments are not included.
            // A requirement that uses a variable, such as `from: version`, is reported as written, and cannot be updated
            var range = new TextRange(tokens[segment.Start].Start, tokens[segment.End - 1].End, IsLiteralRequirement(text, tokens, segment));
            dependencyVersionRange = range;
            return text[range.Start..range.End];
        }

        dependencyVersionRange = null;
        return null;
    }

    private static bool IsRequirementArgument(string text, List<SwiftToken> tokens, TokenRange segment)
    {
        if (TryGetLabel(text, tokens, segment, out var label))
            return segment.Length > 2 && RequirementLabels.Contains(label, StringComparer.Ordinal);

        // .upToNextMajor(from: "1.0.0"), .exact("1.0.0"), Package.Dependency.Requirement.branch("main")
        var index = segment.Start;
        while (index + 1 < segment.End &&
               tokens[index].Kind is SwiftTokenKind.Identifier &&
               RequirementTypeQualifiers.Contains(text[tokens[index].Start..tokens[index].End], StringComparer.Ordinal) &&
               tokens[index + 1].Is(text, SwiftTokenKind.Punctuation, "."))
        {
            index += 2;
        }

        if (index == segment.Start && index < segment.End && tokens[index].Is(text, SwiftTokenKind.Punctuation, "."))
        {
            index++;
        }

        if (index > segment.Start &&
            index + 1 < segment.End &&
            tokens[index].Kind is SwiftTokenKind.Identifier &&
            RequirementFactoryMethods.Contains(text[tokens[index].Start..tokens[index].End], StringComparer.Ordinal) &&
            tokens[index + 1].Is(text, SwiftTokenKind.Punctuation, "("))
        {
            return true;
        }

        // "1.0.0"..<"2.0.0" or "1.0.0"..."2.0.0"
        var depth = 0;
        for (var i = segment.Start; i < segment.End; i++)
        {
            var token = tokens[i];
            if (token.Kind is SwiftTokenKind.Punctuation)
            {
                switch (text[token.Start])
                {
                    case '(' or '[' or '{':
                        depth++;
                        break;
                    case ')' or ']' or '}':
                        depth--;
                        break;
                }
            }
            else if (depth == 0 && (token.Is(text, SwiftTokenKind.Operator, "..<") || token.Is(text, SwiftTokenKind.Operator, "...")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a requirement is made of string literals only, such as <c>.upToNextMajor(from: "1.0.0")</c>, and no variable.</summary>
    private static bool IsLiteralRequirement(string text, List<SwiftToken> tokens, TokenRange segment)
    {
        for (var i = segment.Start; i < segment.End; i++)
        {
            var token = tokens[i];
            var isLiteral = token.Kind switch
            {
                SwiftTokenKind.StringLiteral => token.IsTerminated && IsVerbatim(text, token),
                SwiftTokenKind.Identifier => IsRequirementKeyword(text[token.Start..token.End]),
                SwiftTokenKind.Punctuation or SwiftTokenKind.Operator => true,
                _ => false,
            };

            if (!isLiteral)
                return false;
        }

        return true;

        static bool IsRequirementKeyword(string identifier)
        {
            return RequirementLabels.Contains(identifier, StringComparer.Ordinal)
                || RequirementFactoryMethods.Contains(identifier, StringComparer.Ordinal)
                || RequirementTypeQualifiers.Contains(identifier, StringComparer.Ordinal);
        }
    }

    /// <summary>Whether the value of a string literal is exactly its text, so that the text can be replaced by another value.</summary>
    private static bool IsVerbatim(string text, SwiftToken token)
    {
        return !token.HasInterpolation && text.AsSpan(token.ContentStart, token.ContentEnd - token.ContentStart).SequenceEqual(SwiftLexer.GetStringValue(text, token));
    }

    private static bool TryGetLabel(string text, List<SwiftToken> tokens, TokenRange segment, out string label)
    {
        if (segment.Length >= 2 &&
            tokens[segment.Start].Kind is SwiftTokenKind.Identifier &&
            tokens[segment.Start + 1].Is(text, SwiftTokenKind.Punctuation, ":"))
        {
            label = text[tokens[segment.Start].Start..tokens[segment.Start].End];
            return true;
        }

        label = "";
        return false;
    }

    private static bool TryGetStringLiteral(string text, List<SwiftToken> tokens, int start, int end, out string? value, out TextRange valueRange)
    {
        // The value must be the literal alone: "https://" + host is not the URL "https://"
        if (start + 1 == end && tokens[start] is { Kind: SwiftTokenKind.StringLiteral, IsTerminated: true } token)
        {
            value = SwiftLexer.GetStringValue(text, token);
            valueRange = new TextRange(token.ContentStart, token.ContentEnd, IsVerbatim(text, token));
            return true;
        }

        value = null;
        valueRange = default;
        return false;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TextRange(int Start, int End, bool IsVerbatim = true)
    {
        public int Length => End - Start;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TokenRange(int Start, int End)
    {
        public int Length => End - Start;
    }
}
