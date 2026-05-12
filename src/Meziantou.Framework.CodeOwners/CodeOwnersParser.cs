using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework;

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
/// var entries = CodeOwnersParser.Parse(content).ToArray();
/// // entries[0]: Pattern="*", Member="user1", EntryType=Username
/// // entries[1]: Pattern="*", Member="user2", EntryType=Username
/// // entries[2]: Pattern="*.js", Member="js-owner", EntryType=Username
/// // entries[3]: Pattern="docs/*", Member="docs@example.com", EntryType=EmailAddress
/// </code>
/// </example>
/// </summary>
public static class CodeOwnersParser
{
    /// <summary>Parses the content of a CODEOWNERS file and returns the code owner entries.</summary>
    /// <param name="content">The content of the CODEOWNERS file.</param>
    /// <returns>An enumerable collection of <see cref="CodeOwnersEntry"/> representing the parsed code owners.</returns>
    public static IEnumerable<CodeOwnersEntry> Parse(string content)
    {
        var context = new CodeOwnersParserContext(content);
        return context.Parse();
    }

    [StructLayout(LayoutKind.Auto)]
    private struct CodeOwnersParserContext
    {
        private static readonly SearchValues<char> PatternSeparatorSearchValues = SearchValues.Create(" \t\r\n\\");
        private static readonly SearchValues<char> MemberSeparatorSearchValues = SearchValues.Create(" \t\r\n");

        private readonly List<CodeOwnersEntry> _entries = [];
        private readonly string _content;
        private CodeOwnersSection? _currentSection;
        private int _index;
        private int _patternIndex;

        public CodeOwnersParserContext(string content)
        {
            _content = content;
        }

        public List<CodeOwnersEntry> Parse()
        {
            while (!EndOfFile)
            {
                ParseLine();
            }

            return _entries;
        }

        private void ParseLine()
        {
            if (TryConsumeEndOfLineOrEndOfFile())
                return;

            var c = Peek();

            // Comment
            if (c == '#')
            {
                ConsumeUntil('\n');
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

            // Parse members (username or email)
            ParseMembers(pattern, _patternIndex);
            _patternIndex++;
        }

        private bool TryParseSection(out CodeOwnersSection section)
        {
            if (!EndOfFile)
            {
                if (TryConsumeEndOfLineOrEndOfFile())
                {
                    section = default;
                    return false;
                }

                var isOptional = false;
                var c = Peek();
                if (c == '^')
                {
                    isOptional = true;
                    Consume();
                }

                c = Peek();
                if (c == '[')
                {
                    var name = ParseSectionName();

                    var requiredReviewerCount = 1;
                    if (Peek() == '[')
                    {
                        requiredReviewerCount = ParseSectionRequiredReviewerCount();
                    }

                    var defaultOwners = new List<string>();
                    if (Peek() == ' ')
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
            }

            section = default;
            return false;
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

        private void ParseMembers(string pattern, int patternIndex)
        {
            var foundMember = false;

            while (!EndOfFile)
            {
                ConsumeSpaces();
                if (TryConsumeEndOfLineOrEndOfFile())
                    break;

                var c = Consume();

                // Inline comment
                if (c == '#')
                {
                    ConsumeUntil('\n');
                    break;
                }

                var isMember = c == '@';
                var memberStart = isMember ? _index : _index - 1;

                var remaining = _content.AsSpan(_index);
                var separatorIndex = remaining.IndexOfAny(MemberSeparatorSearchValues);
                if (separatorIndex < 0)
                {
                    AddEntry(isMember, _content.AsSpan(memberStart).ToString(), pattern, patternIndex);
                    _index = _content.Length;
                    return;
                }

                var memberLength = _index + separatorIndex - memberStart;
                var member = _content.AsSpan(memberStart, memberLength).ToString();
                var separator = remaining[separatorIndex];
                _index += separatorIndex;
                if (separator is '\r' or '\n')
                {
                    AddEntry(isMember, member, pattern, patternIndex);
                    _ = TryConsumeEndOfLineOrEndOfFile();
                    return;
                }

                _index++;
                AddEntry(isMember, member, pattern, patternIndex);
                foundMember = true;
            }

            if (!foundMember)
            {
                if (_currentSection.HasValue && _currentSection.Value.HasDefaultOwners)
                {
                    foreach (var defaultOwner in _currentSection.Value.DefaultOwners)
                    {
                        var isMember = defaultOwner[0] == '@';
                        AddEntry(isMember, isMember ? defaultOwner[1..] : defaultOwner, pattern, patternIndex);
                    }
                }
                else
                {
                    AddEntry(isMember: false, name: null, pattern, patternIndex);
                }
            }
        }

        private void AddEntry(bool isMember, string? name, string pattern, int patternIndex)
        {
            if (name is null)
            {
                _entries.Add(CodeOwnersEntry.FromNone(patternIndex, pattern, _currentSection));
            }
            else if (isMember)
            {
                _entries.Add(CodeOwnersEntry.FromUsername(patternIndex, pattern, name, _currentSection));
            }
            else
            {
                _entries.Add(CodeOwnersEntry.FromEmailAddress(patternIndex, pattern, name, _currentSection));
            }
        }

        private string ParseSectionName()
        {
            _ = Consume();
            var remaining = _content.AsSpan(_index);
            var separatorIndex = remaining.IndexOf(']');
            if (separatorIndex < 0)
            {
                _index = _content.Length;
                return remaining.ToString();
            }

            var sectionName = remaining[..separatorIndex].ToString();
            _index += separatorIndex + 1;
            return sectionName;
        }

        private int ParseSectionRequiredReviewerCount()
        {
            if (Peek() == '[')
            {
                _ = Consume();
            }
            else
            {
                // If no count is specified in section headers, only one reviewer is required by default.
                return 1;
            }

            var remaining = _content.AsSpan(_index);
            var separatorIndex = remaining.IndexOf(']');
            ReadOnlySpan<char> requiredReviewerCountText;
            if (separatorIndex < 0)
            {
                requiredReviewerCountText = remaining;
                _index = _content.Length;
            }
            else
            {
                requiredReviewerCountText = remaining[..separatorIndex];
                _index += separatorIndex + 1;
            }

            var isParseValid = int.TryParse(requiredReviewerCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredReviewerCount);
            return isParseValid ? requiredReviewerCount : 1;
        }

        private List<string> ParseSectionDefaultOwners()
        {
            var start = _index;
            ConsumeUntil('\n');
            var length = _index - start;
            if (_index > start && _content[_index - 1] == '\n')
            {
                length--;
            }

            var remaining = _content.AsSpan(start, length).Trim();
            var defaultOwners = new List<string>();
            while (!remaining.IsEmpty)
            {
                var tokenStart = remaining.IndexOfAnyExcept(' ', '\t');
                if (tokenStart < 0)
                    break;

                remaining = remaining[tokenStart..];
                var tokenEnd = remaining.IndexOfAny(' ', '\t');
                ReadOnlySpan<char> token;
                if (tokenEnd < 0)
                {
                    token = remaining;
                    remaining = default;
                }
                else
                {
                    token = remaining[..tokenEnd];
                    remaining = remaining[tokenEnd..];
                }

                // GitLab stops parsing default owners when encountering an unexpected token
                // but keeps the default owners already parsed as valid.
                if (token[0] is '[' or '#')
                    break;

                defaultOwners.Add(token.ToString());
            }

            return defaultOwners;
        }

        private bool EndOfFile => _index >= _content.Length;

        private char? Peek()
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

        private void ConsumeUntil(char character)
        {
            if (EndOfFile)
                return;

            var index = _content.AsSpan(_index).IndexOf(character);
            if (index < 0)
            {
                _index = _content.Length;
                return;
            }

            _index += index + 1;
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
