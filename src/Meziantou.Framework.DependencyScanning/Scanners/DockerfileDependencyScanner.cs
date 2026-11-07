using System.Runtime.InteropServices;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Dockerfile and Containerfile for Docker image dependencies in FROM, COPY --from and RUN --mount=from instructions, and in the syntax directive.
/// Only references with a tag or a digest are reported, so build stage names are not mistaken for images.
/// </summary>
public sealed class DockerfileDependencyScanner : DependencyScanner
{
    private static readonly string[] FileNames = ["Dockerfile", "Containerfile"];
    private static readonly string[] Extensions = [".Dockerfile", ".Containerfile"];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.DockerImage];

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        var directives = ParseDirectives(text);
        if (directives.Syntax is { } syntax)
        {
            // # syntax=docker/dockerfile:1 is the image of the frontend that builds the Dockerfile
            Report(context, text, syntax, offset: 0, syntax.Length, requireTagOrDigest: false);
        }

        var instructions = ReadInstructions(text, directives.EscapeCharacter, context.CancellationToken);
        var globalArguments = GetGlobalArguments(text, instructions);

        // An argument used by several FROM instructions cannot be updated for one image without changing the others
        var argumentUsages = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tokens in instructions)
        {
            if (GetFromImageToken(text, tokens) is { } imageToken && GetArgumentReference(imageToken.GetText(text).ToString(), globalArguments) is { } reference)
            {
                argumentUsages[reference.Argument.Name] = argumentUsages.GetValueOrDefault(reference.Argument.Name) + 1;
            }
        }

        foreach (var tokens in instructions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var keyword = tokens[0].GetText(text);
            if (keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                if (GetFromImageToken(text, tokens) is { } imageToken)
                {
                    if (GetArgumentReference(imageToken.GetText(text).ToString(), globalArguments) is { } reference)
                    {
                        ReportArgumentReference(context, text, imageToken, reference, isUpdatable: argumentUsages[reference.Argument.Name] is 1);
                    }
                    else
                    {
                        Report(context, text, imageToken, offset: 0, imageToken.Length, requireTagOrDigest: true);
                    }
                }
            }
            else if (keyword.Equals("COPY", StringComparison.OrdinalIgnoreCase))
            {
                // COPY [--from=<image|stage|context>] [--chown=...] <src> ... <dest>
                for (var index = 1; index < tokens.Length; index++)
                {
                    var token = tokens[index];
                    var tokenText = token.GetText(text);
                    if (!tokenText.StartsWith("--", StringComparison.Ordinal))
                        break;

                    if (tokenText.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))
                    {
                        Report(context, text, token, offset: "--from=".Length, token.Length - "--from=".Length, requireTagOrDigest: true);
                        break;
                    }

                    if (tokenText.Equals("--from", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length)
                    {
                        Report(context, text, tokens[index + 1], offset: 0, tokens[index + 1].Length, requireTagOrDigest: true);
                        break;
                    }
                }
            }
            else if (keyword.Equals("RUN", StringComparison.OrdinalIgnoreCase))
            {
                // RUN [--mount=type=bind,from=<image|stage|context>,...] <command>
                for (var index = 1; index < tokens.Length; index++)
                {
                    var token = tokens[index];
                    var tokenText = token.GetText(text);
                    if (!tokenText.StartsWith("--", StringComparison.Ordinal))
                        break;

                    if (tokenText.StartsWith("--mount=", StringComparison.OrdinalIgnoreCase))
                    {
                        ReportMountSource(context, text, token, "--mount=".Length);
                    }
                }
            }
        }
    }

    private static List<Token[]> ReadInstructions(string text, char escapeCharacter, CancellationToken cancellationToken)
    {
        var result = new List<Token[]>();
        var reader = new InstructionReader(text, escapeCharacter);
        var tokens = new List<Token>();
        while (reader.ReadInstruction(tokens))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tokens.Count is 0)
                continue;

            result.Add([.. tokens]);

            // https://docs.docker.com/reference/dockerfile/#here-documents
            // The bodies of the here-documents of an instruction follow it, in the order of their markers
            var keyword = tokens[0].GetText(text);
            if (keyword.Equals("RUN", StringComparison.OrdinalIgnoreCase) || keyword.Equals("COPY", StringComparison.OrdinalIgnoreCase) || keyword.Equals("ADD", StringComparison.OrdinalIgnoreCase))
            {
                for (var index = 1; index < tokens.Count; index++)
                {
                    if (TryParseHeredocMarker(tokens[index].GetText(text), out var delimiter, out var stripLeadingTabs))
                    {
                        reader.SkipHeredoc(delimiter, stripLeadingTabs);
                    }
                }
            }
        }

        return result;
    }

    // <<EOF, <<-EOF, <<"EOF", <<'EOF', and 3<<EOF with a file descriptor
    private static bool TryParseHeredocMarker(ReadOnlySpan<char> token, out string delimiter, out bool stripLeadingTabs)
    {
        delimiter = "";
        stripLeadingTabs = false;

        token = token.TrimStart("0123456789");
        if (!token.StartsWith("<<", StringComparison.Ordinal))
            return false;

        token = token[2..];
        if (token is ['-', ..])
        {
            stripLeadingTabs = true;
            token = token[1..];
        }

        if (token is [var quote, .., var endQuote] && quote is '"' or '\'' && endQuote == quote)
        {
            token = token[1..^1];
        }

        if (token.IsEmpty || token.ContainsAny("<\"'"))
            return false;

        delimiter = token.ToString();
        return true;
    }

    // FROM [--platform=<platform>] <image> [AS <name>]
    private static Token? GetFromImageToken(string text, Token[] tokens)
    {
        if (!tokens[0].GetText(text).Equals("FROM", StringComparison.OrdinalIgnoreCase))
            return null;

        var index = 1;
        while (index < tokens.Length && tokens[index].GetText(text).StartsWith("--", StringComparison.Ordinal))
        {
            index++;
        }

        var remaining = tokens.Length - index - 1;
        if (remaining is 0 || (remaining is 2 && tokens[index + 1].GetText(text).Equals("AS", StringComparison.OrdinalIgnoreCase)))
            return tokens[index];

        return null;
    }

    // Only the ARG instructions before the first FROM declare arguments that FROM can use
    private static Dictionary<string, ArgumentDefault> GetGlobalArguments(string text, List<Token[]> instructions)
    {
        var result = new Dictionary<string, ArgumentDefault>(StringComparer.Ordinal);
        foreach (var tokens in instructions)
        {
            var keyword = tokens[0].GetText(text);
            if (keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase))
                break;

            if (!keyword.Equals("ARG", StringComparison.OrdinalIgnoreCase))
                continue;

            // ARG <name>[=<default value>] ...
            for (var index = 1; index < tokens.Length; index++)
            {
                var token = tokens[index];
                var tokenText = token.GetText(text);
                var equalsIndex = tokenText.IndexOf('=');
                var name = (equalsIndex < 0 ? tokenText : tokenText[..equalsIndex]).ToString();
                if (equalsIndex < 0)
                {
                    // A declaration without a default value does not tell which value is used
                    result.Remove(name);
                    continue;
                }

                var valueOffset = equalsIndex + 1;
                var value = tokenText[valueOffset..];
                if (value is [var quote, .., var endQuote] && quote is '"' or '\'' && endQuote == quote)
                {
                    valueOffset++;
                    value = value[1..^1];
                }

                // Only a literal value can be reported and updated
                if (value.IsEmpty || value.ContainsAny("$\"'`\\"))
                {
                    result.Remove(name);
                    continue;
                }

                result[name] = new ArgumentDefault(name, value.ToString(), token, valueOffset);
            }
        }

        return result;
    }

    // The whole image (FROM ${BASE_IMAGE}) or the whole tag (FROM node:${NODE_VERSION}) comes from an argument
    private static ArgumentReference? GetArgumentReference(string value, Dictionary<string, ArgumentDefault> arguments)
    {
        if (arguments.Count is 0)
            return null;

        if (GetVariableName(value) is { } imageArgument)
            return arguments.TryGetValue(imageArgument, out var argument) ? new ArgumentReference(argument, IsWholeImage: true) : null;

        if (DockerImageReference.TryParse(value, out var reference) && reference.Digest is null && reference.Tag is { } tag && GetVariableName(tag) is { } tagArgument && arguments.TryGetValue(tagArgument, out var tagArgumentDefault))
            return new ArgumentReference(tagArgumentDefault, IsWholeImage: false);

        return null;

        static string? GetVariableName(string value)
        {
            ReadOnlySpan<char> name;
            if (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}', StringComparison.Ordinal))
            {
                name = value.AsSpan(2, value.Length - 3);
            }
            else if (value.StartsWith('$', StringComparison.Ordinal))
            {
                name = value.AsSpan(1);
            }
            else
            {
                return null;
            }

            if (name.IsEmpty || char.IsAsciiDigit(name[0]))
                return null;

            foreach (var c in name)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not '_')
                    return null;
            }

            return name.ToString();
        }
    }

    private void ReportArgumentReference(ScanFileContext context, string text, Token imageToken, ArgumentReference reference, bool isUpdatable)
    {
        var argument = reference.Argument;
        if (reference.IsWholeImage)
        {
            DockerImageReference.Report(this, context, argument.Value, GetArgumentLocation, requireTagOrDigest: true);
            return;
        }

        if (!DockerImageReference.TryParse(imageToken.GetText(text).ToString(), out var imageReference))
            return;

        Location nameLocation = imageReference.Name.Contains('$', StringComparison.Ordinal)
            ? new NonUpdatableLocation(context)
            : new TextLocation(context.FileSystem, context.FullPath, imageToken.Line, imageToken.Column, imageReference.Name.Length);
        context.ReportDependency(this, imageReference.Name, argument.Value, DependencyType.DockerImage, nameLocation, GetArgumentLocation(0, argument.Value.Length));

        Location GetArgumentLocation(int start, int length)
        {
            return isUpdatable
                ? new TextLocation(context.FileSystem, context.FullPath, argument.Token.Line, argument.Token.Column + argument.ValueOffset + start, length)
                : new NonUpdatableLocation(context);
        }
    }

    private void ReportMountSource(ScanFileContext context, string text, Token token, int offset)
    {
        // The options are comma-separated key=value pairs. Quoted values are not supported.
        var options = token.GetText(text)[offset..];
        if (options.ContainsAny('"', '\''))
            return;

        while (!options.IsEmpty)
        {
            var commaIndex = options.IndexOf(',');
            var option = commaIndex < 0 ? options : options[..commaIndex];
            if (option.StartsWith("from=", StringComparison.OrdinalIgnoreCase))
            {
                // Stage names cannot contain ':' or '@', so requiring a tag or a digest skips them
                Report(context, text, token, offset + "from=".Length, option.Length - "from=".Length, requireTagOrDigest: true);
                return;
            }

            if (commaIndex < 0)
                break;

            offset += commaIndex + 1;
            options = options[(commaIndex + 1)..];
        }
    }

    private void Report(ScanFileContext context, string text, Token token, int offset, int length, bool requireTagOrDigest)
    {
        var value = text.Substring(token.Index + offset, length);
        DockerImageReference.Report(
            this,
            context,
            value,
            (start, length) => new TextLocation(context.FileSystem, context.FullPath, token.Line, token.Column + offset + start, length),
            requireTagOrDigest);
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // <name>.Dockerfile
        if (context.HasExtension(Extensions, ignoreCase: true))
            return true;

        foreach (var fileName in FileNames)
        {
            if (context.HasFileName(fileName, ignoreCase: true))
                return true;

            // Dockerfile.<name>
            if (context.FileName.Length > fileName.Length &&
                context.FileName[fileName.Length] is '.' &&
                context.FileName.StartsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // https://docs.docker.com/reference/dockerfile/#parser-directives
    private static Directives ParseDirectives(string text)
    {
        var escapeCharacter = '\\';
        Token? syntax = null;
        var index = 0;
        var lineNumber = 1;
        while (index < text.Length)
        {
            var lineStart = index;
            var remaining = text.AsSpan(index);
            var lineEnd = remaining.IndexOfAny('\r', '\n');
            var rawLine = lineEnd < 0 ? remaining : remaining[..lineEnd];
            if (lineEnd < 0)
            {
                index = text.Length;
            }
            else
            {
                index += lineEnd + 1;

                // \r\n is a single line break
                if (text[index - 1] is '\r' && index < text.Length && text[index] is '\n')
                {
                    index++;
                }
            }

            // Directives must be at the top of the file, and the first line that is not a directive ends them
            var line = rawLine.TrimStart();
            if (line is not ['#', .. var directive])
                break;

            directive = directive.TrimEnd();
            var equalsIndex = directive.IndexOf('=');
            if (equalsIndex < 0)
                break;

            var name = directive[..equalsIndex].Trim();
            if (name.IsEmpty || name.ContainsAny(' ', '\t'))
                break;

            var rawValue = directive[(equalsIndex + 1)..];
            var value = rawValue.TrimStart();
            if (name.Equals("escape", StringComparison.OrdinalIgnoreCase))
            {
                escapeCharacter = value is "`" ? '`' : '\\';
            }
            else if (name.Equals("syntax", StringComparison.OrdinalIgnoreCase) && !value.IsEmpty)
            {
                var valueIndex = lineStart + (rawLine.Length - line.Length) + 1 + equalsIndex + 1 + (rawValue.Length - value.Length);
                syntax = new Token(valueIndex, value.Length, lineNumber, valueIndex - lineStart + 1);
            }

            lineNumber++;
        }

        return new Directives(escapeCharacter, syntax);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Directives(char EscapeCharacter, Token? Syntax);

    private sealed record ArgumentDefault(string Name, string Value, Token Token, int ValueOffset);

    private sealed record ArgumentReference(ArgumentDefault Argument, bool IsWholeImage);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Token(int Index, int Length, int Line, int Column)
    {
        public ReadOnlySpan<char> GetText(string text) => text.AsSpan(Index, Length);
    }

    /// <summary>Splits a Dockerfile into instructions made of whitespace-separated tokens, joining continuation lines and skipping comments. It runs in linear time.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct InstructionReader(string text, char escapeCharacter)
    {
        private int _index;
        private int _line = 1;
        private int _lineStart;

        public bool ReadInstruction(List<Token> tokens)
        {
            tokens.Clear();

            // Skip empty lines and comments
            while (true)
            {
                SkipSpaces();
                if (_index >= text.Length)
                    return false;

                if (IsLineBreak(text[_index]))
                {
                    ConsumeLineBreak();
                }
                else if (text[_index] is '#')
                {
                    SkipLine();
                }
                else
                {
                    break;
                }
            }

            while (true)
            {
                SkipSpaces();
                if (_index >= text.Length)
                    return true;

                var c = text[_index];
                if (IsLineBreak(c))
                {
                    ConsumeLineBreak();
                    return true;
                }

                if (c == escapeCharacter && IsLineContinuation(_index))
                {
                    SkipLine();

                    // Comment lines and empty lines inside a continued instruction are ignored
                    while (true)
                    {
                        SkipSpaces();
                        if (_index < text.Length && IsLineBreak(text[_index]))
                        {
                            ConsumeLineBreak();
                        }
                        else if (_index < text.Length && text[_index] is '#')
                        {
                            SkipLine();
                        }
                        else
                        {
                            break;
                        }
                    }

                    continue;
                }

                var start = _index;
                while (_index < text.Length)
                {
                    c = text[_index];
                    if (c is ' ' or '\t' || IsLineBreak(c) || (c == escapeCharacter && IsLineContinuation(_index)))
                        break;

                    _index++;
                }

                tokens.Add(new Token(start, _index - start, _line, start - _lineStart + 1));
            }
        }

        /// <summary>Skips the body of a here-document, up to and including the line that holds its delimiter.</summary>
        public void SkipHeredoc(string delimiter, bool stripLeadingTabs)
        {
            while (_index < text.Length)
            {
                var start = _index;
                while (_index < text.Length && !IsLineBreak(text[_index]))
                {
                    _index++;
                }

                var line = text.AsSpan(start, _index - start);
                if (_index < text.Length)
                {
                    ConsumeLineBreak();
                }

                if (stripLeadingTabs)
                {
                    line = line.TrimStart('\t');
                }

                if (line.SequenceEqual(delimiter))
                    return;
            }
        }

        private readonly bool IsLineContinuation(int escapeIndex)
        {
            for (var i = escapeIndex + 1; i < text.Length; i++)
            {
                var c = text[i];
                if (IsLineBreak(c))
                    return true;

                if (c is not (' ' or '\t'))
                    return false;
            }

            return true;
        }

        private void SkipSpaces()
        {
            while (_index < text.Length && text[_index] is ' ' or '\t')
            {
                _index++;
            }
        }

        private void SkipLine()
        {
            while (_index < text.Length && !IsLineBreak(text[_index]))
            {
                _index++;
            }

            if (_index < text.Length)
            {
                ConsumeLineBreak();
            }
        }

        private void ConsumeLineBreak()
        {
            if (text[_index] is '\r' && _index + 1 < text.Length && text[_index + 1] is '\n')
            {
                _index++;
            }

            _index++;
            _line++;
            _lineStart = _index;
        }

        private static bool IsLineBreak(char c) => c is '\r' or '\n';
    }
}
