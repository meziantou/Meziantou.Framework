using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>
/// Parses CODEOWNERS files used by GitHub and GitLab to define code ownership.
/// <example>
/// <code>
/// var content = """
///     * @user1 @user2
///     *.js @js-owner
///     docs/* docs@example.com
///     """;
/// var entries = CodeOwnersParser.Parse(content);
/// // entries[0]: Pattern="*", Owners=[@user1, @user2]
/// // entries[1]: Pattern="*.js", Owners=[@js-owner]
/// // entries[2]: Pattern="docs/*", Owners=[docs@example.com]
/// </code>
/// </example>
/// </summary>
public static class CodeOwnersParser
{
    /// <summary>Parses the content of a CODEOWNERS file and returns the code owner entries.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <returns>The <see cref="CodeOwnersEntry"/> instances representing the parsed code owners.</returns>
    /// <exception cref="CodeOwnersParseException"><paramref name="content"/> is not a valid CODEOWNERS file. Parsing stops at the first error.</exception>
    public static IReadOnlyList<CodeOwnersEntry> Parse(string content)
    {
        var context = new CodeOwnersParserContext(content);
        var entries = context.Parse();
        if (context.HasError)
            throw new CodeOwnersParseException(context.CreateError());

        return entries;
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="entries">When this method returns <see langword="true"/>, contains the <see cref="CodeOwnersEntry"/> instances representing the parsed code owners; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    /// <remarks>Use the <see cref="TryParse(string, out IReadOnlyList{CodeOwnersEntry}, out CodeOwnersError)"/> overload to know why the file is invalid.</remarks>
    public static bool TryParse(string content, [NotNullWhen(true)] out IReadOnlyList<CodeOwnersEntry>? entries)
    {
        return TryParse(content, out entries, out _);
    }

    /// <summary>Parses the content of a CODEOWNERS file and returns a value indicating whether it is valid.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <param name="entries">When this method returns <see langword="true"/>, contains the <see cref="CodeOwnersEntry"/> instances representing the parsed code owners; otherwise, <see langword="null"/>.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains the first error found in <paramref name="content"/>; otherwise, <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="content"/> is a valid CODEOWNERS file; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string content, [NotNullWhen(true)] out IReadOnlyList<CodeOwnersEntry>? entries, out CodeOwnersError error)
    {
        var context = new CodeOwnersParserContext(content);
        var result = context.Parse();
        if (context.HasError)
        {
            entries = null;
            error = context.CreateError();
            return false;
        }

        entries = result;
        error = default;
        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct CodeOwnersParserContext
    {
        private static readonly SearchValues<char> PatternSeparatorSearchValues = SearchValues.Create(" \t\r\n\\");
        private static readonly SearchValues<char> MemberSeparatorSearchValues = SearchValues.Create(" \t\r\n");

        private readonly List<CodeOwnersEntry> _entries = [];
        private readonly string _content;
        private (CodeOwnersErrorKind Kind, int Index)? _error;
        private CodeOwnersSection? _currentSection;
        private int _index;

        public CodeOwnersParserContext(string content)
        {
            _content = content;
        }

        public List<CodeOwnersEntry> Parse()
        {
            while (!EndOfFile && _error is null)
            {
                ParseLine();
            }

            return _entries;
        }

        public readonly bool HasError => _error is not null;

        /// <summary>Records the first error found. Parsing stops as soon as one is set.</summary>
        private void SetError(CodeOwnersErrorKind kind, int index)
        {
            _error ??= (kind, index);
        }

        public readonly CodeOwnersError CreateError()
        {
            var (kind, errorIndex) = _error.GetValueOrDefault();

            // The character offset is only turned into a line and a position when the file is actually invalid.
            var lineNumber = 1;
            var lineStartIndex = 0;
            var index = 0;
            while (index < errorIndex)
            {
                var c = _content[index];
                if (c is '\r')
                {
                    index++;
                    if (index < _content.Length && _content[index] is '\n')
                    {
                        index++;
                    }
                }
                else if (c is '\n')
                {
                    index++;
                }
                else
                {
                    index++;
                    continue;
                }

                lineNumber++;
                lineStartIndex = index;
            }

            return new CodeOwnersError(kind, lineNumber, Math.Max(1, errorIndex - lineStartIndex + 1));
        }

        private void ParseLine()
        {
            if (TryConsumeEndOfLineOrEndOfFile())
                return;

            var c = Peek();

            // Comment
            if (c == '#')
            {
                ConsumeUntilEndOfLineOrEndOfFile();
                return;
            }

            // Section
            if (TryParseSection(out var section))
            {
                _currentSection = section;
                return;
            }

            // Parse pattern
            var pattern = ParsePattern();
            if (string.IsNullOrEmpty(pattern))
                return;

            ParseEntry(pattern);
        }

        private void ParseEntry(string pattern)
        {
            var owners = ParseOwners();
            IReadOnlyList<CodeOwner> entryOwners;
            if (owners is not null)
            {
                entryOwners = owners;
            }
            else if (_currentSection.HasValue && _currentSection.Value.HasDefaultOwners)
            {
                // A pattern that declares no owner inherits the ones declared on the section header.
                // The list is shared: it is never mutated once the section is parsed.
                entryOwners = _currentSection.Value.DefaultOwners;
            }
            else
            {
                // The pattern is explicitly left unowned
                entryOwners = [];
            }

            _entries.Add(new CodeOwnersEntry(pattern, entryOwners, _currentSection));
        }

        private bool TryParseSection(out CodeOwnersSection section)
        {
            // The line may not be a section after all, in which case everything consumed here must be restored
            // so the line can be parsed as a pattern.
            var startIndex = _index;

            var isOptional = false;
            if (Peek() == '^')
            {
                isOptional = true;
                _ = Consume();
            }

            if (Peek() == '[' && TryParseSectionName(out var name))
            {
                var requiredReviewerCount = 1;
                if (Peek() == '[')
                {
                    requiredReviewerCount = ParseSectionRequiredReviewerCount();
                }

                var defaultOwners = new List<CodeOwner>();
                if (Peek() is ' ' or '\t')
                {
                    defaultOwners = ParseSectionDefaultOwners();
                }
                else
                {
                    ConsumeUntilEndOfLineOrEndOfFile();
                }

                section = new CodeOwnersSection(name, isOptional ? 0 : requiredReviewerCount, defaultOwners);
                return true;
            }

            _index = startIndex;
            section = default;
            return false;
        }

        private bool TryParseSectionName(out string name)
        {
            var startIndex = _index;
            _ = Consume();

            // A section header cannot span multiple lines. Without this bound, an unclosed '[' would consume
            // the remaining lines of the file and silently discard them.
            var remaining = _content.AsSpan(_index);
            var separatorIndex = remaining.IndexOfAny(']', '\r', '\n');
            if (separatorIndex < 0 || remaining[separatorIndex] is not ']')
            {
                SetError(CodeOwnersErrorKind.UnterminatedSectionHeader, startIndex);
                _index = startIndex;
                name = string.Empty;
                return false;
            }

            name = remaining[..separatorIndex].ToString();
            _index += separatorIndex + 1;
            return true;
        }

        private int ParseSectionRequiredReviewerCount()
        {
            var startIndex = _index;
            _ = Consume();

            var remaining = _content.AsSpan(_index);
            var separatorIndex = remaining.IndexOfAny(']', '\r', '\n');
            if (separatorIndex < 0 || remaining[separatorIndex] is not ']')
            {
                SetError(CodeOwnersErrorKind.UnterminatedRequiredReviewerCount, startIndex);
                _index = startIndex;
                return 1;
            }

            var requiredReviewerCountText = remaining[..separatorIndex];
            _index += separatorIndex + 1;

            // A count of 0 is how an optional section is represented, so it cannot be allowed here: it would make
            // a section that is not prefixed by '^' report itself as optional.
            if (!int.TryParse(requiredReviewerCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredReviewerCount) || requiredReviewerCount < 1)
            {
                SetError(CodeOwnersErrorKind.InvalidRequiredReviewerCount, startIndex);
                return 1;
            }

            return requiredReviewerCount;
        }

        private List<CodeOwner> ParseSectionDefaultOwners()
        {
            var lineStartIndex = _index;
            var line = ConsumeLine();

            var defaultOwners = new List<CodeOwner>();
            var offset = 0;
            while (offset < line.Length)
            {
                var tokenStart = line[offset..].IndexOfAnyExcept(' ', '\t');
                if (tokenStart < 0)
                    break;

                offset += tokenStart;
                var remaining = line[offset..];
                var tokenEnd = remaining.IndexOfAny(' ', '\t');
                var token = tokenEnd < 0 ? remaining : remaining[..tokenEnd];

                // GitLab stops parsing default owners when encountering an unexpected token
                // but keeps the default owners already parsed as valid.
                if (token[0] is '[' or '#')
                    break;

                var defaultOwner = token.ToString();
                if (defaultOwner is "@")
                {
                    SetError(CodeOwnersErrorKind.EmptyOwner, lineStartIndex + offset);
                }
                else if (defaultOwner[0] is '@')
                {
                    defaultOwners.Add(CodeOwner.Username(defaultOwner[1..]));
                }
                else if (IsEmailAddress(defaultOwner))
                {
                    defaultOwners.Add(CodeOwner.EmailAddress(defaultOwner));
                }
                else
                {
                    SetError(CodeOwnersErrorKind.InvalidOwner, lineStartIndex + offset);
                }

                offset += token.Length;
            }

            return defaultOwners;
        }

        private string? ParsePattern()
        {
            var remaining = _content.AsSpan(_index);
            var separatorIndex = remaining.IndexOfAny(PatternSeparatorSearchValues);
            if (separatorIndex < 0)
            {
                _index = _content.Length;
                return remaining.ToString();
            }

            var separator = remaining[separatorIndex];
            if (separator is not '\\')
            {
                var pattern = remaining[..separatorIndex].ToString();
                _index += separatorIndex;
                if (separator is ' ' or '\t')
                {
                    _index++;
                }

                return pattern;
            }

            Span<char> initialBuffer = stackalloc char[128];
            using var sb = new ValueStringBuilder(initialBuffer);
            while (!EndOfFile)
            {
                var c = Peek();
                if (c is null or '\r' or '\n')
                    return sb.ToString();

                c = Consume();
                if (c is null)
                    return sb.ToString();

                switch (c)
                {
                    // The next character is escaped
                    case '\\':
                        c = Consume();
                        if (c is null) // end of file
                            return sb.ToString();

                        sb.Append(c.GetValueOrDefault());
                        break;

                    case ' ':
                    case '\t':
                        return sb.ToString();

                    default:
                        sb.Append(c.GetValueOrDefault());
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>Parses the owners declared on the current line, or returns <see langword="null"/> when the line declares none.</summary>
        private List<CodeOwner>? ParseOwners()
        {
            List<CodeOwner>? owners = null;

            while (!EndOfFile)
            {
                ConsumeSpaces();
                if (TryConsumeEndOfLineOrEndOfFile())
                    break;

                var c = Consume();

                // Inline comment
                if (c == '#')
                {
                    ConsumeUntilEndOfLineOrEndOfFile();
                    break;
                }

                var isUsername = c == '@';
                var tokenStartIndex = _index - 1;
                var ownerStart = isUsername ? _index : tokenStartIndex;

                var remaining = _content.AsSpan(_index);
                var separatorIndex = remaining.IndexOfAny(MemberSeparatorSearchValues);
                string owner;
                char? separator;
                if (separatorIndex < 0)
                {
                    owner = _content.AsSpan(ownerStart).ToString();
                    _index = _content.Length;
                    separator = null;
                }
                else
                {
                    var ownerLength = _index + separatorIndex - ownerStart;
                    owner = _content.AsSpan(ownerStart, ownerLength).ToString();
                    separator = remaining[separatorIndex];
                    _index += separatorIndex;
                }

                if (owner.Length is 0)
                {
                    // A lone '@' does not identify anybody
                    SetError(CodeOwnersErrorKind.EmptyOwner, tokenStartIndex);
                }
                else if (isUsername)
                {
                    owners ??= [];
                    owners.Add(CodeOwner.Username(owner));
                }
                else if (IsEmailAddress(owner))
                {
                    owners ??= [];
                    owners.Add(CodeOwner.EmailAddress(owner));
                }
                else
                {
                    SetError(CodeOwnersErrorKind.InvalidOwner, tokenStartIndex);
                }

                if (separator is null)
                    break;

                if (separator is '\r' or '\n')
                {
                    _ = TryConsumeEndOfLineOrEndOfFile();
                    break;
                }

                _index++;
            }

            return owners;
        }

        private static bool IsEmailAddress(ReadOnlySpan<char> value)
        {
            var index = value.IndexOf('@');
            return index > 0 && index < value.Length - 1;
        }

        private readonly bool EndOfFile => _index >= _content.Length;

        private readonly char? Peek()
        {
            if (_index >= _content.Length)
                return null;

            return _content[_index];
        }

        private char? Consume()
        {
            if (_index >= _content.Length)
            {
                return null;
            }

            return _content[_index++];
        }

        private bool TryConsumeEndOfLineOrEndOfFile()
        {
            if (_index >= _content.Length)
            {
                return true;
            }

            var c = _content[_index];
            if (c == '\r')
            {
                _index++;
                if (_index < _content.Length && _content[_index] == '\n')
                {
                    _index++;
                }

                return true;
            }

            if (c == '\n')
            {
                _index++;
                return true;
            }

            return false;
        }

        /// <summary>Consumes the current line, including its line ending, and returns it without its line ending.</summary>
        private ReadOnlySpan<char> ConsumeLine()
        {
            var remaining = _content.AsSpan(_index);
            var endOfLineIndex = remaining.IndexOfAny('\r', '\n');
            if (endOfLineIndex < 0)
            {
                _index = _content.Length;
                return remaining;
            }

            _index += endOfLineIndex;
            _ = TryConsumeEndOfLineOrEndOfFile();
            return remaining[..endOfLineIndex];
        }

        private void ConsumeUntilEndOfLineOrEndOfFile()
        {
            if (EndOfFile)
                return;

            var endOfLineIndex = _content.AsSpan(_index).IndexOfAny('\r', '\n');
            if (endOfLineIndex < 0)
            {
                _index = _content.Length;
                return;
            }

            _index += endOfLineIndex;
            _ = TryConsumeEndOfLineOrEndOfFile();
        }

        private void ConsumeSpaces()
        {
            if (EndOfFile)
                return;

            var nextNonWhitespaceIndex = _content.AsSpan(_index).IndexOfAnyExcept(' ', '\t');
            if (nextNonWhitespaceIndex < 0)
            {
                _index = _content.Length;
                return;
            }

            _index += nextNonWhitespaceIndex;
        }
    }
}
