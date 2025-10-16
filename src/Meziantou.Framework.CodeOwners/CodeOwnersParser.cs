using System.Runtime.InteropServices;
using Microsoft.Extensions.ObjectPool;

namespace Meziantou.Framework.CodeOwners;

public static class CodeOwnersParser
{
    public static IEnumerable<CodeOwnersEntry> Parse(string content)
    {
        var context = new CodeOwnersParserContext(content);
        return context.Parse();
    }

    [StructLayout(LayoutKind.Auto)]
    private struct CodeOwnersParserContext
    {
        private static readonly ObjectPool<StringBuilder> StringBuilderPool = CreateStringBuilderPool();

        private readonly List<CodeOwnersEntry> _entries = [];
        private readonly StringLexer _lexer;
        private CodeOwnersSection? _currentSection;
        private int _index;

        private static ObjectPool<StringBuilder> CreateStringBuilderPool()
        {
            var objectPoolProvider = new DefaultObjectPoolProvider();
            return objectPoolProvider.CreateStringBuilderPool();
        }

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
            var sb = StringBuilderPool.Get();
            while (!_lexer.EndOfFile)
            {
                var c = _lexer.Peek();
                if (c is null || c == '\r' || c == '\n')
                    return StringBuilderPool.ToStringAndReturn(sb);

                c = _lexer.Consume();
                switch (c)
                {
                    // The next character is escaped
                    case '\\':
                        c = _lexer.Consume();
                        if (c is null) // end of file
                            return StringBuilderPool.ToStringAndReturn(sb);

                        sb.Append(c);
                        break;

                    case ' ':
                    case '\t':
                        return StringBuilderPool.ToStringAndReturn(sb);

                    default:
                        sb.Append(c);
                        break;
                }
            }

            return StringBuilderPool.ToStringAndReturn(sb);
        }

        private readonly void ParseMembers(string pattern, int patternIndex)
        {
            var foundMember = false;

            while (!_lexer.EndOfFile)
            {
                _lexer.ConsumeSpaces();
                if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    break;

                var sb = StringBuilderPool.Get();

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
                    sb.Append(c);
                }

                while (!_lexer.EndOfFile)
                {
                    if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    {
                        AddEntry(isMember, StringBuilderPool.ToStringAndReturn(sb), pattern, patternIndex);
                        return;
                    }

                    c = _lexer.Consume();
                    if (c is ' ' or '\t')
                    {
                        AddEntry(isMember, StringBuilderPool.ToStringAndReturn(sb), pattern, patternIndex);
                        foundMember = true;
                        break;
                    }

                    sb.Append(c);
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
            var sb = StringBuilderPool.Get();
            _lexer.ConsumeUntil(']', sb);

            return StringBuilderPool.ToStringAndReturn(sb);
        }

        private readonly int ParseSectionRequiredReviewerCount()
        {
            var sb = StringBuilderPool.Get();

            var c = _lexer.Peek();
            if (c == '[')
            {
                _lexer.Consume();
                _lexer.ConsumeUntil(']', sb);
            }
            else
            {
                // If no count is specified in section headers, only one reviewer is required by default.
                return 1;
            }

            var requiredReviewerCountString = StringBuilderPool.ToStringAndReturn(sb);
            var isParseValid = int.TryParse(requiredReviewerCountString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int requiredReviewerCount);
            return isParseValid ? requiredReviewerCount : 1;
        }

        private readonly List<string> ParseSectionDefaultOwners()
        {
            var sb = StringBuilderPool.Get();
            _lexer.ConsumeUntil('\n', sb);
            var defaultOwnersString = StringBuilderPool.ToStringAndReturn(sb).Trim();

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

        public void ConsumeUntil(char character, StringBuilder sb)
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
                if (next != ' ' && next != '\t')
                    return;

                _currentIndex++;
            }
        }
    }
}
