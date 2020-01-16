using System.Collections.Generic;
using System.Text;

namespace Meziantou.Framework.CodeOwners
{
    public static class CodeOwnersParser
    {
        public static IEnumerable<CodeOwnersEntry> Parse(string content)
        {
            var context = new CodeOwnersParserContext(content);
            return context.Parse();
        }

        private sealed class CodeOwnersParserContext
        {
            private readonly string _content;
            private readonly StringBuilder _builder = new StringBuilder();
            private readonly List<CodeOwnersEntry> _entries = new List<CodeOwnersEntry>();
            private int _currentIndex = -1;

            public CodeOwnersParserContext(string content)
            {
                _content = content;
            }

            private bool EndOfFile => _currentIndex >= _content.Length;

            private char? Consume()
            {
                if (_currentIndex + 1 >= _content.Length)
                {
                    _currentIndex++; // Ensure EOF is set correctly
                    return null;
                }

                _currentIndex++;
                return _content[_currentIndex];
            }

            private char? Peek()
            {
                if (_currentIndex + 1 >= _content.Length)
                    return null;

                return _content[_currentIndex + 1];
            }

            private void SkipUntil(char character)
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

            private void SkipSpaces()
            {
                while (_currentIndex + 1 < _content.Length)
                {
                    var next = _content[_currentIndex + 1];
                    if (next != ' ' && next != '\t')
                        return;

                    _currentIndex++;
                }
            }

            public IEnumerable<CodeOwnersEntry> Parse()
            {
                while (!EndOfFile)
                {
                    ParseLine();
                }

                return _entries;
            }

            private bool TryConsumeEndOfLineOrEndOfFile()
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

            private void ParseLine()
            {
                if (TryConsumeEndOfLineOrEndOfFile())
                    return;

                // Comment
                var c = Peek();
                if (c == '#')
                {
                    SkipUntil('\n');
                    return;
                }

                // Parse pattern
                var pattern = ParsePattern();
                if (pattern == null)
                    return;

                // Parse members (username or email)
                foreach (var (name, isMember) in ParseMembers())
                {
                    if (isMember)
                    {
                        _entries.Add(CodeOwnersEntry.FromUsername(pattern, name));
                    }
                    else
                    {
                        _entries.Add(CodeOwnersEntry.FromEmailAddress(pattern, name));
                    }
                }
            }

            private string? ParsePattern()
            {
                var sb = _builder.Clear();
                while (!EndOfFile)
                {
                    if (TryConsumeEndOfLineOrEndOfFile())
                        return null;

                    var c = Consume();
                    switch (c)
                    {
                        // The next character is escaped
                        case '\\':
                            c = Consume();
                            if (c == null) // end of file
                                return null;

                            sb.Append(c);
                            break;

                        case ' ':
                        case '\t':
                            return sb.ToString();

                        default:
                            sb.Append(c);
                            break;
                    }
                }

                return sb.ToString();
            }

            private IEnumerable<(string Name, bool IsMember)> ParseMembers()
            {
                while (!EndOfFile)
                {
                    SkipSpaces();
                    if (TryConsumeEndOfLineOrEndOfFile())
                        yield break;

                    var sb = _builder.Clear();

                    var c = Consume();
                    var isMember = c == '@';
                    if (!isMember)
                        sb.Append(c);

                    while (!EndOfFile)
                    {
                        if (TryConsumeEndOfLineOrEndOfFile())
                        {
                            yield return (sb.ToString(), isMember);
                            yield break;
                        }

                        c = Consume();
                        if (c == ' ' || c == '\t')
                        {
                            yield return (sb.ToString(), isMember);
                            break;
                        }

                        sb.Append(c);
                    }
                }
            }
        }
    }
}
