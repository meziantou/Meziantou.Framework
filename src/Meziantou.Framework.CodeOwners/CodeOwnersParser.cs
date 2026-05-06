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
        private readonly List<CodeOwnersEntry> _entries = [];
        private readonly StringLexer _lexer;
        private CodeOwnersSection? _currentSection;
        private int _index;

        public CodeOwnersParserContext(string content)
        {
            _lexer = new StringLexer(content);
        }

        public List<CodeOwnersEntry> Parse()
        {
            while (!_lexer.EndOfFile)
            {
                ParseLine();
            }

            return _entries;
        }

        private void ParseLine()
        {
            if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                return;

            var c = _lexer.Peek();

            // Comment
            if (c == '#')
            {
                _lexer.ConsumeUntil('\n');
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
            ParseMembers(pattern, _index);
            _index++;
        }

        private readonly bool TryParseSection(out CodeOwnersSection section)
        {
            if (!_lexer.EndOfFile)
            {
                if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                {
                    section = default;
                    return false;
                }

                var isOptional = false;
                var c = _lexer.Peek();
                if (c == '^')
                {
                    isOptional = true;
                    _lexer.Consume();
                }

                c = _lexer.Peek();
                if (c == '[')
                {
                    var name = ParseSectionName();

                    var requiredReviewerCount = 1;
                    if (_lexer.Peek() == '[')
                    {
                        requiredReviewerCount = ParseSectionRequiredReviewerCount();
                    }

                    var defaultOwners = new List<string>();
                    if (_lexer.Peek() == ' ')
                    {
                        defaultOwners = ParseSectionDefaultOwners();
                    }
                    else
                    {
                        _lexer.ConsumeUntilEndOfLineOrEndOfFile();
                    }

                    section = new CodeOwnersSection(name, isOptional ? 0 : requiredReviewerCount, defaultOwners);
                    return true;
                }
            }

            section = default;
            return false;
        }

        private readonly string? ParsePattern()
        {
            Span<char> initialBuffer = stackalloc char[128];
            using var sb = new ValueStringBuilder(initialBuffer);
            while (!_lexer.EndOfFile)
            {
                var c = _lexer.Peek();
                if (c is null or '\r' or '\n')
                    return sb.ToString();

                c = _lexer.Consume();
                if (c is null)
                    return sb.ToString();

                switch (c)
                {
                    // The next character is escaped
                    case '\\':
                        c = _lexer.Consume();
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

        private readonly void ParseMembers(string pattern, int patternIndex)
        {
            var foundMember = false;

            while (!_lexer.EndOfFile)
            {
                _lexer.ConsumeSpaces();
                if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    break;

                using var sb = new ValueStringBuilder(initialCapacity: 128);

                var c = _lexer.Consume();

                // Inline comment
                if (c == '#')
                {
                    _lexer.ConsumeUntil('\n');
                    break;
                }

                var isMember = c == '@';
                if (!isMember)
                {
                    if (c is not null)
                    {
                        sb.Append(c.GetValueOrDefault());
                    }
                }

                while (!_lexer.EndOfFile)
                {
                    if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    {
                        AddEntry(isMember, sb.ToString(), pattern, patternIndex);
                        return;
                    }

                    c = _lexer.Consume();
                    if (c is ' ' or '\t')
                    {
                        AddEntry(isMember, sb.ToString(), pattern, patternIndex);
                        foundMember = true;
                        break;
                    }

                    if (c is not null)
                    {
                        sb.Append(c.GetValueOrDefault());
                    }
                }
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

        private readonly void AddEntry(bool isMember, string? name, string pattern, int patternIndex)
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

        private readonly string ParseSectionName()
        {
            _lexer.Consume();
            Span<char> initialBuffer = stackalloc char[32];
            var sb = new ValueStringBuilder(initialBuffer);
            try
            {
                _lexer.ConsumeUntil(']', ref sb);
                return sb.AsSpan().ToString();
            }
            finally
            {
                sb.Dispose();
            }
        }

        private readonly int ParseSectionRequiredReviewerCount()
        {
            Span<char> initialBuffer = stackalloc char[16];
            var sb = new ValueStringBuilder(initialBuffer);
            try
            {
                var c = _lexer.Peek();
                if (c == '[')
                {
                    _lexer.Consume();
                    _lexer.ConsumeUntil(']', ref sb);
                }
                else
                {
                    // If no count is specified in section headers, only one reviewer is required by default.
                    return 1;
                }

                var requiredReviewerCountString = sb.AsSpan().ToString();
                var isParseValid = int.TryParse(requiredReviewerCountString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredReviewerCount);
                return isParseValid ? requiredReviewerCount : 1;
            }
            finally
            {
                sb.Dispose();
            }
        }

        private readonly List<string> ParseSectionDefaultOwners()
        {
            Span<char> initialBuffer = stackalloc char[128];
            var sb = new ValueStringBuilder(initialBuffer);
            string defaultOwnersString;
            try
            {
                _lexer.ConsumeUntil('\n', ref sb);
                defaultOwnersString = sb.AsSpan().ToString().Trim();
            }
            finally
            {
                sb.Dispose();
            }

            var defaultOwners = new List<string>();
            if (!string.IsNullOrEmpty(defaultOwnersString))
            {
                var splits = defaultOwnersString
                    .Replace('\t', ' ')
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var split in splits)
                {
                    // GitLab stops parsing default owners when encountering an unexpected token
                    // but keeps the default owners already parsed as valid.
                    if (split.StartsWith('[') || split.StartsWith('#'))
                        break;

                    defaultOwners.Add(split);
                }
            }

            return defaultOwners;
        }
    }

    private sealed class StringLexer
    {
        private int _currentIndex = -1;
        private readonly string _content;

        public StringLexer(string content)
        {
            _content = content;
        }

        public bool EndOfFile => _currentIndex >= _content.Length;

        public bool TryConsumeEndOfLineOrEndOfFile()
        {
            var c = Peek();
            if (c is null)
            {
                Consume();
                return true;
            }

            if (c == '\r')
            {
                Consume();
                if (Peek() == '\n')
                    Consume();

                return true;
            }

            if (c == '\n')
            {
                Consume();
                return true;
            }

            return false;
        }

        public char? Consume()
        {
            if (_currentIndex + 1 >= _content.Length)
            {
                _currentIndex++; // Ensure EOF is set correctly
                return null;
            }

            _currentIndex++;
            return _content[_currentIndex];
        }

        public char? Peek()
        {
            if (_currentIndex + 1 >= _content.Length)
                return null;

            return _content[_currentIndex + 1];
        }

        public void ConsumeUntil(char character)
        {
            while (_currentIndex + 1 < _content.Length)
            {
                var next = _content[_currentIndex + 1];
                if (next == character)
                {
                    _currentIndex++;
                    return;
                }

                _currentIndex++;
            }
        }

        public void ConsumeUntil(char character, ref ValueStringBuilder sb)
        {
            while (_currentIndex + 1 < _content.Length)
            {
                var next = _content[_currentIndex + 1];
                if (next == character)
                {
                    _currentIndex++;
                    return;
                }

                sb.Append(next);
                _currentIndex++;
            }
        }

        public void ConsumeUntilEndOfLineOrEndOfFile()
        {
            while (!TryConsumeEndOfLineOrEndOfFile())
            {
                Consume();
            }
        }

        public void ConsumeSpaces()
        {
            while (_currentIndex + 1 < _content.Length)
            {
                var next = _content[_currentIndex + 1];
                if (next is not ' ' and not '\t')
                    return;

                _currentIndex++;
            }
        }
    }
}
