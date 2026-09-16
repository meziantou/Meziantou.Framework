using System.Runtime.InteropServices;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Dockerfile and Containerfile for Docker image dependencies in FROM and COPY --from instructions.
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

        var reader = new InstructionReader(text, GetEscapeCharacter(text));
        var tokens = new List<Token>();
        while (reader.ReadInstruction(tokens))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (tokens.Count is 0)
                continue;

            var keyword = tokens[0].GetText(text);
            if (keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase))
            {
                // FROM [--platform=<platform>] <image> [AS <name>]
                var index = 1;
                while (index < tokens.Count && tokens[index].GetText(text).StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                }

                var remaining = tokens.Count - index - 1;
                if (remaining is 0 || (remaining is 2 && tokens[index + 1].GetText(text).Equals("AS", StringComparison.OrdinalIgnoreCase)))
                {
                    Report(context, text, tokens[index], offset: 0);
                }
            }
            else if (keyword.Equals("COPY", StringComparison.OrdinalIgnoreCase))
            {
                // COPY [--from=<image|stage|context>] [--chown=...] <src> ... <dest>
                for (var index = 1; index < tokens.Count; index++)
                {
                    var token = tokens[index];
                    var tokenText = token.GetText(text);
                    if (!tokenText.StartsWith("--", StringComparison.Ordinal))
                        break;

                    if (tokenText.StartsWith("--from=", StringComparison.OrdinalIgnoreCase))
                    {
                        Report(context, text, token, offset: "--from=".Length);
                        break;
                    }

                    if (tokenText.Equals("--from", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Count)
                    {
                        Report(context, text, tokens[index + 1], offset: 0);
                        break;
                    }
                }
            }
        }
    }

    private void Report(ScanFileContext context, string text, Token token, int offset)
    {
        var value = text.Substring(token.Index + offset, token.Length - offset);
        DockerImageReference.Report(
            this,
            context,
            value,
            (start, length) => new TextLocation(context.FileSystem, context.FullPath, token.Line, token.Column + offset + start, length),
            requireTagOrDigest: true);
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
    private static char GetEscapeCharacter(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var remaining = text.AsSpan(index);
            var lineEnd = remaining.IndexOfAny('\r', '\n');
            var line = (lineEnd < 0 ? remaining : remaining[..lineEnd]).Trim();
            index = lineEnd < 0 ? text.Length : index + lineEnd + 1;

            // Directives must be at the top of the file, and the first line that is not a directive ends them
            if (line is not ['#', .. var directive])
                break;

            var equalsIndex = directive.IndexOf('=');
            if (equalsIndex < 0)
                break;

            var name = directive[..equalsIndex].Trim();
            if (name.IsEmpty || name.ContainsAny(' ', '\t'))
                break;

            if (name.Equals("escape", StringComparison.OrdinalIgnoreCase))
                return directive[(equalsIndex + 1)..].Trim() is "`" ? '`' : '\\';
        }

        return '\\';
    }

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
