using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Swift Package Manager files (Package.resolved and Package.swift) for dependencies.</summary>
public sealed class SwiftPackageDependencyScanner : DependencyScanner
{
    private const string PackageResolvedFileName = "Package.resolved";
    private const string PackageSwiftFileName = "Package.swift";

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.SwiftPackage];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName(PackageResolvedFileName, ignoreCase: false) ||
               context.HasFileName(PackageSwiftFileName, ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var fileName = Path.GetFileName(context.FullPath);
        if (string.Equals(fileName, PackageResolvedFileName, StringComparison.Ordinal))
        {
            await ScanPackageResolvedAsync(context).ConfigureAwait(false);
            return;
        }

        if (string.Equals(fileName, PackageSwiftFileName, StringComparison.Ordinal))
        {
            await ScanPackageSwiftAsync(context).ConfigureAwait(false);
        }
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
                var dependencyVersion = GetDependencyVersion(pin, out var dependencyVersionPath);
                if (dependencyName is null && dependencyVersion is null)
                    continue;

                context.ReportDependency(this, dependencyName, dependencyVersion, DependencyType.SwiftPackage,
                    nameLocation: dependencyNamePath is null ? null : new JsonLocation(context, dependencyNamePath),
                    versionLocation: dependencyVersionPath is null ? null : new JsonLocation(context, dependencyVersionPath));
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

        foreach (var packageCall in EnumeratePackageCalls(text))
        {
            var segments = SplitTopLevelArguments(text, packageCall.ArgumentsStart, packageCall.ArgumentsEnd);

            var dependencyName = GetDependencyName(text, segments, out var dependencyNameRange);
            var dependencyVersion = GetDependencyVersion(text, segments, out var dependencyVersionRange);
            if (dependencyName is null && dependencyVersion is null)
                continue;

            context.ReportDependency(this, dependencyName, dependencyVersion, DependencyType.SwiftPackage,
                nameLocation: dependencyNameRange is { } nameRange ? CreateLocation(context, text, nameRange) : null,
                versionLocation: dependencyVersionRange is { } versionRange ? CreateLocation(context, text, versionRange) : null);
        }
    }

    private static Location CreateLocation(ScanFileContext context, string text, TextRange range)
    {
        if (range.Length <= 0 || ContainsNewLine(text, range))
            return new NonUpdatableLocation(context);

        return CreateTextLocation(context, text, range);
    }

    private static TextLocation CreateTextLocation(ScanFileContext context, string text, TextRange range)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < range.Start; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new TextLocation(context.FileSystem, context.FullPath, line, range.Start - lineStart + 1, range.Length);
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

    private static string? GetDependencyVersion(JsonObject pin, out string? dependencyVersionPath)
    {
        dependencyVersionPath = null;

        if (!JsonNodeDocument.TryGetObject(pin, "state", out var state))
            return null;

        if (TryGetString(state, "version", out var version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "branch", out version, out dependencyVersionPath))
            return version;

        if (TryGetString(state, "revision", out version, out dependencyVersionPath))
            return version;

        return null;
    }

    private static bool TryGetString(JsonObject jsonObject, string propertyName, out string? value, out string? valuePath)
    {
        if (JsonNodeDocument.TryGetProperty(jsonObject, propertyName, out var node) &&
            node is not null &&
            JsonNodeDocument.TryGetString(node, out var stringValue))
        {
            value = stringValue;
            valuePath = node.GetPath();
            return true;
        }

        value = null;
        valuePath = null;
        return false;
    }

    private static IEnumerable<PackageCall> EnumeratePackageCalls(string text)
    {
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c != '.' || !text.AsSpan(i).StartsWith(".package", StringComparison.Ordinal))
                continue;

            if (i > 0 && IsIdentifierCharacter(text[i - 1]))
                continue;

            var openParenthesisIndex = i + ".package".Length;
            while (openParenthesisIndex < text.Length && char.IsWhiteSpace(text[openParenthesisIndex]))
            {
                openParenthesisIndex++;
            }

            if (openParenthesisIndex >= text.Length || text[openParenthesisIndex] != '(')
                continue;

            if (!TryFindMatchingParenthesis(text, openParenthesisIndex, out var closeParenthesisIndex))
                continue;

            yield return new PackageCall(openParenthesisIndex + 1, closeParenthesisIndex);
            i = closeParenthesisIndex;
        }
    }

    private static bool TryFindMatchingParenthesis(string text, int openingParenthesisIndex, out int closingParenthesisIndex)
    {
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        var depth = 1;

        for (var i = openingParenthesisIndex + 1; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    closingParenthesisIndex = i;
                    return true;
                }
            }
        }

        closingParenthesisIndex = -1;
        return false;
    }

    private static List<TextRange> SplitTopLevelArguments(string text, int start, int end)
    {
        var result = new List<TextRange>();
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var segmentStart = start;

        for (var i = start; i < end; i++)
        {
            var c = text[i];
            var next = i + 1 < end ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            switch (c)
            {
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    break;
                case ',' when parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    result.Add(new TextRange(segmentStart, i));
                    segmentStart = i + 1;
                    break;
            }
        }

        if (segmentStart <= end)
        {
            result.Add(new TextRange(segmentStart, end));
        }

        return result;
    }

    private static string? GetDependencyName(string text, List<TextRange> segments, out TextRange? dependencyNameRange)
    {
        foreach (var label in new[] { "name", "id", "url", "location", "path" })
        {
            foreach (var segment in segments)
            {
                if (TryGetLabeledStringArgument(text, segment, label, out var value, out var valueRange))
                {
                    dependencyNameRange = valueRange;
                    return value;
                }
            }
        }

        if (segments.Count > 0 && TryGetUnlabeledStringArgument(text, segments[0], out var unlabeledValue, out var unlabeledValueRange))
        {
            dependencyNameRange = unlabeledValueRange;
            return unlabeledValue;
        }

        dependencyNameRange = null;
        return null;
    }

    private static string? GetDependencyVersion(string text, List<TextRange> segments, out TextRange? dependencyVersionRange)
    {
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var trimmed = Trim(text, segments[i]);
            if (trimmed.Length == 0 || IsSourceArgument(text, trimmed))
                continue;

            dependencyVersionRange = trimmed;
            return text[trimmed.Start..trimmed.End];
        }

        dependencyVersionRange = null;
        return null;
    }

    private static bool IsSourceArgument(string text, TextRange range)
    {
        return HasLabel(text, range, "name") ||
               HasLabel(text, range, "id") ||
               HasLabel(text, range, "url") ||
               HasLabel(text, range, "location") ||
               HasLabel(text, range, "path");
    }

    private static bool HasLabel(string text, TextRange range, string label)
    {
        var start = range.Start;
        var end = range.End;
        if (end - start <= label.Length || !text.AsSpan(start).StartsWith(label, StringComparison.Ordinal))
            return false;

        var index = start + label.Length;
        while (index < end && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index < end && text[index] == ':';
    }

    private static bool TryGetLabeledStringArgument(string text, TextRange range, string label, out string? value, out TextRange valueRange)
    {
        var trimmed = Trim(text, range);
        if (!HasLabel(text, trimmed, label))
        {
            value = null;
            valueRange = default;
            return false;
        }

        var valueStart = trimmed.Start + label.Length;
        while (valueStart < trimmed.End && char.IsWhiteSpace(text[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= trimmed.End || text[valueStart] != ':')
        {
            value = null;
            valueRange = default;
            return false;
        }

        valueStart++;
        while (valueStart < trimmed.End && char.IsWhiteSpace(text[valueStart]))
        {
            valueStart++;
        }

        return TryReadStringLiteral(text, valueStart, trimmed.End, out value, out valueRange);
    }

    private static bool TryGetUnlabeledStringArgument(string text, TextRange range, out string? value, out TextRange valueRange)
    {
        var trimmed = Trim(text, range);
        return TryReadStringLiteral(text, trimmed.Start, trimmed.End, out value, out valueRange);
    }

    private static bool TryReadStringLiteral(string text, int start, int end, out string? value, out TextRange valueRange)
    {
        if (start >= end || text[start] != '"')
        {
            value = null;
            valueRange = default;
            return false;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] == '"')
            {
                value = text[(start + 1)..i];
                valueRange = new TextRange(start + 1, i);
                return true;
            }
        }

        value = null;
        valueRange = default;
        return false;
    }

    private static TextRange Trim(string text, TextRange range)
    {
        var start = range.Start;
        var end = range.End;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new TextRange(start, end);
    }

    private static bool IsIdentifierCharacter(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct TextRange
    {
        public TextRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }
        public int End { get; }
        public int Length => End - Start;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct PackageCall
    {
        public PackageCall(int argumentsStart, int argumentsEnd)
        {
            ArgumentsStart = argumentsStart;
            ArgumentsEnd = argumentsEnd;
        }

        public int ArgumentsStart { get; }
        public int ArgumentsEnd { get; }
    }
}
