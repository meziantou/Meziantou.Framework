using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans .NET file-based app (.cs) files for package, SDK, project, reference, and target framework directives.</summary>
public sealed class DotNetFileBasedAppDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } =
    [
        DependencyType.NuGet,
        DependencyType.MSBuildProjectReference,
        DependencyType.DotNetAssemblyReference,
        DependencyType.DotNetTargetFramework,
    ];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasExtension(".cs", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        var isInsideMultilineComment = false;
        var tokens = new List<DirectiveToken>();
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;

            // Skip leading whitespace and comments. offset is the start of the remaining text in the line.
            var offset = 0;
            var hasCommentOnLine = false;
            while (true)
            {
                if (isInsideMultilineComment)
                {
                    var commentEndIndex = line.IndexOf("*/", offset, StringComparison.Ordinal);
                    if (commentEndIndex < 0)
                    {
                        offset = line.Length;
                        break;
                    }

                    offset = commentEndIndex + 2;
                    isInsideMultilineComment = false;
                    hasCommentOnLine = true;
                }

                while (offset < line.Length && char.IsWhiteSpace(line[offset]))
                {
                    offset++;
                }

                if (!line.AsSpan(offset).StartsWith("/*", StringComparison.Ordinal))
                    break;

                offset += 2;
                isInsideMultilineComment = true;
                hasCommentOnLine = true;
            }

            var remainingLine = line.AsSpan(offset);

            // Skip empty lines, comments, and shebang lines at the top of the file
            if (remainingLine.IsWhiteSpace() || remainingLine.StartsWith("//", StringComparison.Ordinal) || remainingLine.StartsWith("#!", StringComparison.Ordinal))
                continue;

            // The SDK reads the directives in the leading trivia of the first token of the file, so it stops at the first token.
            // Like any preprocessor directive, a directive must be the first non-whitespace text of its line, so a directive after a comment is ignored by the compiler.
            if (hasCommentOnLine || remainingLine[0] is not '#')
                break;

            if (!remainingLine.StartsWith("#:", StringComparison.Ordinal))
            {
                // Other preprocessor directives, such as #nullable, #region or #pragma, are trivia like comments, so the directives after them are read.
                // The SDK stops at #if, as the directives of an inactive branch must not be read, and the compiler rejects the ones after #if.
                if (IsIfDirective(remainingLine))
                    break;

                continue;
            }

            ScanDirective(context, line, lineNo, offset + 2, tokens);
        }
    }

    private static bool IsIfDirective(ReadOnlySpan<char> line)
    {
        // Whitespace is allowed between '#' and the directive name
        var directive = line[1..].TrimStart();
        return directive.StartsWith("if", StringComparison.Ordinal)
            && (directive.Length == 2 || !(char.IsLetterOrDigit(directive[2]) || directive[2] is '_'));
    }


    private void ScanDirective(ScanFileContext context, string line, int lineNo, int start, List<DirectiveToken> tokens)
    {
        // Like the SDK, the text after "#:" is trimmed, and its first whitespace separates the kind of the directive from its text.
        // The text is read from the raw line, so the reported columns are columns in the raw line.
        var end = line.Length;
        while (end > start && char.IsWhiteSpace(line[end - 1]))
        {
            end--;
        }

        var kindStart = SkipWhiteSpace(line, start, end);
        var kindEnd = FindTokenEnd(line, kindStart, end);
        var textStart = SkipWhiteSpace(line, kindEnd, end);

        tokens.Clear();
        switch (line.AsSpan(kindStart, kindEnd - kindStart))
        {
            case "package":
                if (TryTokenize(line, textStart, end, nameSeparator: '@', allowMetadata: true, tokens))
                {
                    ScanPackageDirective(context, lineNo, tokens);
                }

                break;

            case "sdk":
                if (TryTokenize(line, textStart, end, nameSeparator: '@', allowMetadata: false, tokens))
                {
                    ScanSdkDirective(context, lineNo, tokens);
                }

                break;

            case "project":
                if (TryTokenize(line, textStart, end, nameSeparator: null, allowMetadata: true, tokens))
                {
                    ScanPathDirective(context, lineNo, tokens, DependencyType.MSBuildProjectReference);
                }

                break;

            case "ref":
                if (TryTokenize(line, textStart, end, nameSeparator: null, allowMetadata: true, tokens))
                {
                    ScanPathDirective(context, lineNo, tokens, DependencyType.DotNetAssemblyReference);
                }

                break;

            case "property":
                if (TryTokenize(line, textStart, end, nameSeparator: '=', allowMetadata: false, tokens))
                {
                    ScanPropertyDirective(context, lineNo, tokens);
                }

                break;
        }
    }

    // #:package Name, #:package Name@Version or #:package Name Version=1.0.0, optionally followed by other item metadata
    private void ScanPackageDirective(ScanFileContext context, int lineNo, List<DirectiveToken> tokens)
    {
        if (!TryGetNameAndValue(tokens, out var name, out var version) || !HasValidMetadata(tokens, conflictingName: version is null ? null : "Version"))
            return;

        // The metadata are the ones of a PackageReference item, so the version can also come from Version or VersionOverride
        var reported = false;
        foreach (var candidate in (ReadOnlySpan<DirectivePart?>)[version ?? GetMetadata(tokens, "Version"), GetMetadata(tokens, "VersionOverride")])
        {
            if (candidate?.Trim() is { Text.Length: > 0 } value)
            {
                context.ReportDependency(this, name.Text, value.Text, DependencyType.NuGet, CreateLocation(context, lineNo, name), CreateLocation(context, lineNo, value));
                reported = true;
            }
        }

        if (!reported)
        {
            context.ReportDependency(this, name.Text, version: null, DependencyType.NuGet, CreateLocation(context, lineNo, name), versionLocation: null);
        }
    }

    // #:sdk Name or #:sdk Name@Version
    private void ScanSdkDirective(ScanFileContext context, int lineNo, List<DirectiveToken> tokens)
    {
        if (tokens.Count > 1 || !TryGetNameAndValue(tokens, out var name, out var version))
            return;

        // Like Sdk="Name" in a project file, an SDK without a version is not reported: it is either a built-in SDK or its version comes from global.json.
        if (version?.Trim() is { Text.Length: > 0 } value)
        {
            context.ReportDependency(this, name.Text, value.Text, DependencyType.NuGet, CreateLocation(context, lineNo, name), CreateLocation(context, lineNo, value));
        }
    }

    // #:project Path or #:ref Path, optionally followed by item metadata
    private void ScanPathDirective(ScanFileContext context, int lineNo, List<DirectiveToken> tokens, DependencyType type)
    {
        if (tokens.Count == 0 || !HasValidMetadata(tokens, conflictingName: null))
            return;

        var path = tokens[0].Name.Trim();
        if (path.Text.Length > 0)
        {
            context.ReportDependency(this, path.Text, version: null, type, CreateLocation(context, lineNo, path), versionLocation: null);
        }
    }

    // #:property TargetFramework=Value
    private void ScanPropertyDirective(ScanFileContext context, int lineNo, List<DirectiveToken> tokens)
    {
        if (tokens.Count > 1 || !TryGetNameAndValue(tokens, out var name, out var propertyValue))
            return;

        if (name.Text.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) && propertyValue?.Trim() is { Text.Length: > 0 } value)
        {
            context.ReportDependency(this, name: null, value.Text, DependencyType.DotNetTargetFramework, nameLocation: null, CreateLocation(context, lineNo, value));
        }
    }

    private static Location CreateLocation(ScanFileContext context, int lineNo, DirectivePart part)
    {
        // The value of a quoted part that contains escape sequences is not its text in the file, so it cannot be updated in place
        if (!part.IsVerbatim)
            return new NonUpdatableLocation(context);

        return new TextLocation(context.FileSystem, context.FullPath, lineNo, part.Index + 1, part.Text.Length);
    }

    private static bool TryGetNameAndValue(List<DirectiveToken> tokens, out DirectivePart name, out DirectivePart? value)
    {
        if (tokens.Count == 0)
        {
            name = default;
            value = null;
            return false;
        }

        (name, value) = tokens[0];
        return name.Text.Length > 0 && !ContainsDisallowedNameCharacter(name.Text);
    }

    private static bool ContainsDisallowedNameCharacter(string name)
    {
        // The SDK rejects the names that contain a character that looks like a separator
        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c) || c is '@' or '=' or '/')
                return true;
        }

        return false;
    }

    /// <summary>Checks that every token after the first one is a <c>Name=Value</c> item metadata, as the SDK rejects the directive otherwise.</summary>
    private static bool HasValidMetadata(List<DirectiveToken> tokens, string? conflictingName)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            var (name, value) = tokens[i];
            if (value is null || !IsValidMSBuildName(name.Text) || name.Text.Equals(conflictingName, StringComparison.OrdinalIgnoreCase))
                return false;

            for (var j = 1; j < i; j++)
            {
                if (name.Text.Equals(tokens[j].Name.Text, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private static DirectivePart? GetMetadata(List<DirectiveToken> tokens, string name)
    {
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i].Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return tokens[i].Value;
        }

        return null;
    }

    private static bool IsValidMSBuildName(ReadOnlySpan<char> name)
    {
        // MSBuild property and metadata names are XML NCNames
        if (name.IsEmpty || !XmlConvert.IsStartNCNameChar(name[0]))
            return false;

        foreach (var c in name[1..])
        {
            if (!XmlConvert.IsNCNameChar(c))
                return false;
        }

        return true;
    }

    /// <summary>Splits the text of a directive into whitespace-separated tokens, like the SDK. The first token is a name and an optional value joined by <paramref name="nameSeparator"/>, and the next ones are <c>Name=Value</c> item metadata.</summary>
    /// <returns><see langword="false"/> when the SDK rejects the text.</returns>
    private static bool TryTokenize(string line, int start, int end, char? nameSeparator, bool allowMetadata, List<DirectiveToken> tokens)
    {
        // Quotes were not allowed before the SDK supported quoted values, so a quote means that the text uses the strict syntax
        if (line.AsSpan(start, end - start).Contains('"'))
            return TryTokenizeQuoted(line, start, end, nameSeparator, allowMetadata, tokens);

        if (start == end)
            return true;

        var firstTokenEnd = FindTokenEnd(line, start, end);
        if (firstTokenEnd != end && !(allowMetadata && AreAllMetadata(line, firstTokenEnd, end)))
        {
            // Before the SDK supported quoted values and metadata, the whole text was a single value that could contain whitespace.
            // It still is when the text is not a value followed by metadata.
            tokens.Add(CreateToken(line, start, end, nameSeparator));
            return true;
        }

        var separator = nameSeparator;
        for (var tokenStart = start; tokenStart < end;)
        {
            var tokenEnd = FindTokenEnd(line, tokenStart, end);
            tokens.Add(CreateToken(line, tokenStart, tokenEnd, separator));
            tokenStart = SkipWhiteSpace(line, tokenEnd, end);
            separator = '=';
        }

        return true;
    }

    private static bool AreAllMetadata(string line, int start, int end)
    {
        for (var tokenStart = SkipWhiteSpace(line, start, end); tokenStart < end;)
        {
            var tokenEnd = FindTokenEnd(line, tokenStart, end);
            var token = line.AsSpan(tokenStart, tokenEnd - tokenStart);
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex < 0 || !IsValidMSBuildName(token[..separatorIndex]))
                return false;

            tokenStart = SkipWhiteSpace(line, tokenEnd, end);
        }

        return true;
    }

    private static DirectiveToken CreateToken(string line, int start, int end, char? separator)
    {
        var separatorIndex = separator is { } value ? line.AsSpan(start, end - start).IndexOf(value) : -1;
        if (separatorIndex < 0)
            return new DirectiveToken(CreatePart(line, start, end), Value: null);

        separatorIndex += start;

        // Only a value that contains whitespace can have whitespace around its separator, which is not part of the name and the value
        var nameEnd = separatorIndex;
        while (nameEnd > start && char.IsWhiteSpace(line[nameEnd - 1]))
        {
            nameEnd--;
        }

        return new DirectiveToken(CreatePart(line, start, nameEnd), CreatePart(line, SkipWhiteSpace(line, separatorIndex + 1, end), end));
    }

    private static DirectivePart CreatePart(string line, int start, int end) => new(line[start..end], start, IsVerbatim: true);

    /// <summary>Splits a text that contains quotes. Each side of the separator of a token is either bare or entirely quoted, and whitespace is allowed around the separator.</summary>
    private static bool TryTokenizeQuoted(string line, int start, int end, char? nameSeparator, bool allowMetadata, List<DirectiveToken> tokens)
    {
        var tokenStarted = false;
        var quoteClosed = false;
        var afterSeparator = false;
        DirectivePart? name = null; // Set when the separator of the current token is read
        DirectivePart? quotedPart = null;
        var partStart = 0;
        var partEnd = 0;

        for (var i = start; i < end; i++)
        {
            var c = line[i];
            var separator = tokens.Count == 0 ? nameSeparator : '=';
            if (c is '"')
            {
                // A quoted part must be the whole token or one side of the separator of the token
                if (quoteClosed || (tokenStarted && !afterSeparator))
                    return false;

                if (!TryReadStringLiteral(line, i, out var literalEnd, out var value))
                    return false;

                quotedPart = new DirectivePart(value, i + 1, IsVerbatim: line.AsSpan(i + 1, literalEnd - i - 2).SequenceEqual(value));
                tokenStarted = true;
                quoteClosed = true;
                afterSeparator = false;
                i = literalEnd - 1;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!tokenStarted)
                    continue;

                // Whitespace around the separator does not end the token: "Name = Value" is the same as "Name=Value".
                // After the separator, it still ends the token when a metadata follows, so that an empty value does not swallow the metadata ("Package@ ExcludeAssets=runtime").
                var isAroundSeparator = afterSeparator
                    ? !(allowMetadata && StartsMetadataToken(line, i + 1, end))
                    : name is null && IsNextNonWhiteSpace(line, i + 1, end, separator);
                if (!isAroundSeparator)
                {
                    EndToken();
                }

                continue;
            }

            // Only the first separator splits the token, the next ones belong to the value ("A=b=c")
            if (name is null && c == separator)
            {
                name = GetCurrentPart();
                quotedPart = null;
                partStart = partEnd = i + 1;
                tokenStarted = true;
                quoteClosed = false;
                afterSeparator = true;
                continue;
            }

            // Only the separator can follow a closing quote
            if (quoteClosed)
                return false;

            if (partStart == partEnd)
            {
                partStart = i;
            }

            partEnd = i + 1;
            tokenStarted = true;
            afterSeparator = false;
        }

        if (tokenStarted)
        {
            EndToken();
        }

        return true;

        // The characters of a bare part are contiguous: whitespace is only skipped around the separator
        DirectivePart GetCurrentPart() => quotedPart ?? CreatePart(line, partStart, partEnd);

        void EndToken()
        {
            var part = GetCurrentPart();
            tokens.Add(name is { } tokenName ? new DirectiveToken(tokenName, part) : new DirectiveToken(part, Value: null));
            name = null;
            quotedPart = null;
            partStart = partEnd = 0;
            tokenStarted = false;
            quoteClosed = false;
            afterSeparator = false;
        }
    }

    private static bool IsNextNonWhiteSpace(string line, int start, int end, char? value)
    {
        var index = SkipWhiteSpace(line, start, end);
        return index < end && line[index] == value;
    }

    private static bool StartsMetadataToken(string line, int start, int end)
    {
        var nameStart = SkipWhiteSpace(line, start, end);
        var nameEnd = nameStart;
        while (nameEnd < end && !char.IsWhiteSpace(line[nameEnd]) && line[nameEnd] is not '=')
        {
            nameEnd++;
        }

        // Whitespace is allowed before the separator of a metadata
        var separatorIndex = SkipWhiteSpace(line, nameEnd, end);
        return separatorIndex < end && line[separatorIndex] is '=' && IsValidMSBuildName(line.AsSpan(nameStart, nameEnd - nameStart));
    }

    /// <summary>Reads the regular C# string literal that starts at <paramref name="start"/>. Like the SDK, verbatim and raw string literals are not supported.</summary>
    private static bool TryReadStringLiteral(string line, int start, out int end, [NotNullWhen(true)] out string? value)
    {
        end = 0;
        value = null;
        if (line.AsSpan(start).StartsWith("\"\"\"", StringComparison.Ordinal))
            return false;

        StringBuilder? builder = null;
        var segmentStart = start + 1;
        for (var i = segmentStart; i < line.Length;)
        {
            var c = line[i];
            if (c is '"')
            {
                value = builder is null ? line[segmentStart..i] : builder.Append(line, segmentStart, i - segmentStart).ToString();
                end = i + 1;
                return true;
            }

            if (c is not '\\')
            {
                i++;
                continue;
            }

            builder ??= new StringBuilder();
            builder.Append(line, segmentStart, i - segmentStart);
            if (!TryReadEscapeSequence(line, ref i, builder))
                return false;

            segmentStart = i;
        }

        // The literal is not terminated
        return false;
    }

    private static bool TryReadEscapeSequence(string line, ref int index, StringBuilder builder)
    {
        if (index + 1 >= line.Length)
            return false;

        var c = line[index + 1];
        index += 2;
        uint codePoint;
        switch (c)
        {
            case '\'' or '"' or '\\':
                builder.Append(c);
                return true;

            case '0':
                builder.Append('\0');
                return true;

            case 'a':
                builder.Append('\a');
                return true;

            case 'b':
                builder.Append('\b');
                return true;

            case 'e':
                builder.Append('\u001b');
                return true;

            case 'f':
                builder.Append('\f');
                return true;

            case 'n':
                builder.Append('\n');
                return true;

            case 'r':
                builder.Append('\r');
                return true;

            case 't':
                builder.Append('\t');
                return true;

            case 'v':
                builder.Append('\v');
                return true;

            case 'x' when TryReadHexDigits(line, ref index, minCount: 1, maxCount: 4, out codePoint):
            case 'u' when TryReadHexDigits(line, ref index, minCount: 4, maxCount: 4, out codePoint):
                builder.Append((char)codePoint);
                return true;

            case 'U' when TryReadHexDigits(line, ref index, minCount: 8, maxCount: 8, out codePoint) && codePoint <= 0x10FFFF:
                if (codePoint < 0x10000)
                {
                    builder.Append((char)codePoint);
                }
                else
                {
                    builder.Append((char)(0xD800 + ((codePoint - 0x10000) >> 10)));
                    builder.Append((char)(0xDC00 + ((codePoint - 0x10000) & 0x3FF)));
                }

                return true;

            default:
                return false;
        }
    }

    private static bool TryReadHexDigits(string line, ref int index, int minCount, int maxCount, out uint value)
    {
        var count = 0;
        while (count < maxCount && index + count < line.Length && char.IsAsciiHexDigit(line[index + count]))
        {
            count++;
        }

        if (count < minCount)
        {
            value = 0;
            return false;
        }

        value = uint.Parse(line.AsSpan(index, count), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        index += count;
        return true;
    }

    private static int SkipWhiteSpace(string line, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        return start;
    }

    private static int FindTokenEnd(string line, int start, int end)
    {
        while (start < end && !char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        return start;
    }

    /// <summary>A name or a value of a directive.</summary>
    /// <param name="Text">The value, without the quotes and with the escape sequences decoded.</param>
    /// <param name="Index">The index of the value in the line.</param>
    /// <param name="IsVerbatim">Whether <paramref name="Text"/> is the text at <paramref name="Index"/> in the line.</param>
    private readonly record struct DirectivePart(string Text, int Index, bool IsVerbatim)
    {
        public DirectivePart Trim()
        {
            var trimmedStart = Text.AsSpan().TrimStart();
            var trimmed = trimmedStart.TrimEnd();
            if (trimmed.Length == Text.Length)
                return this;

            return new DirectivePart(trimmed.ToString(), Index + Text.Length - trimmedStart.Length, IsVerbatim);
        }
    }

    /// <summary>A whitespace-separated token of a directive, such as <c>Package@1.0.0</c> or <c>Name=Value</c>. <paramref name="Value"/> is <see langword="null"/> when the token has no separator.</summary>
    private readonly record struct DirectiveToken(DirectivePart Name, DirectivePart? Value);
}
