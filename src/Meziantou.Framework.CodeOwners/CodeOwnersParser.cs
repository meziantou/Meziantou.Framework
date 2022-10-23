using System.Runtime.InteropServices;
using System.Text;
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

        private readonly List<CodeOwnersEntry> _entries;
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
            _entries = new List<CodeOwnersEntry>();
            _lexer = new StringLexer(content);
            _currentSection = null;
        }

        public IEnumerable<CodeOwnersEntry> Parse()
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
            if (pattern == null)
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
                    _lexer.Consume();
                    var sb = StringBuilderPool.Get();
                    _lexer.ConsumeUntil(']', sb);
                    _lexer.ConsumeUntil('\n');
                    section = new CodeOwnersSection(StringBuilderPool.ToStringAndReturn(sb), isOptional);
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
                if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    return null;

                var c = _lexer.Consume();
                switch (c)
                {
                    // The next character is escaped
                    case '\\':
                        c = _lexer.Consume();
                        if (c == null) // end of file
                            return null;

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
            while (!_lexer.EndOfFile)
            {
                _lexer.ConsumeSpaces();
                if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                    return;

                var sb = StringBuilderPool.Get();

                var c = _lexer.Consume();
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
                    if (c == ' ' || c == '\t')
                    {
                        AddEntry(isMember, StringBuilderPool.ToStringAndReturn(sb), pattern, patternIndex);
                        break;
                    }

                    sb.Append(c);
                }
            }
        }

        private readonly void AddEntry(bool isMember, string name, string pattern, int patternIndex)
        {
            if (isMember)
            {
                _entries.Add(CodeOwnersEntry.FromUsername(patternIndex, pattern, name, _currentSection));
            }
            else
            {
                _entries.Add(CodeOwnersEntry.FromEmailAddress(patternIndex, pattern, name, _currentSection));
            }
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
            if (c == null)
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
