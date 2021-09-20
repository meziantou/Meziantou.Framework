using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Meziantou.Framework.CodeOwners
{
    public static class CodeOwnersParser
    {
        public static IEnumerable<CodeOwnersEntry> Parse(string content)
        {
            var context = new CodeOwnersParserContext(content);
            return context.Parse();
        }

        [StructLayout(LayoutKind.Auto)]
        private readonly struct CodeOwnersParserContext
        {
            private static readonly ObjectPool<StringBuilder> s_stringBuilderPool = CreateStringBuilderPool();

            private readonly List<CodeOwnersEntry> _entries;
            private readonly StringLexer _lexer;

            private static readonly char[] _startingCharToIgnore = new char[] { '#', '[', '^' };

            private static ObjectPool<StringBuilder> CreateStringBuilderPool()
            {
                var objectPoolProvider = new DefaultObjectPoolProvider();
                return objectPoolProvider.CreateStringBuilderPool();
            }

            public CodeOwnersParserContext(string content)
            {
                _entries = new List<CodeOwnersEntry>();
                _lexer = new StringLexer(content);
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

                // Comment or section
                var c = _lexer.Peek();
                if (Array.Exists(_startingCharToIgnore, charToIgnore => charToIgnore == c))
                {
                    _lexer.ConsumeUntil('\n');
                    return;
                }

                // Parse pattern
                var pattern = ParsePattern();
                if (pattern == null)
                    return;

                // Parse members (username or email)
                ParseMembers(pattern);
            }

            private string? ParsePattern()
            {
                var sb = s_stringBuilderPool.Get();
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
                            return s_stringBuilderPool.ToStringAndReturn(sb);

                        default:
                            sb.Append(c);
                            break;
                    }
                }

                return s_stringBuilderPool.ToStringAndReturn(sb);
            }

            private void ParseMembers(string pattern)
            {
                while (!_lexer.EndOfFile)
                {
                    _lexer.ConsumeSpaces();
                    if (_lexer.TryConsumeEndOfLineOrEndOfFile())
                        return;

                    var sb = s_stringBuilderPool.Get();

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
                            AddEntry(isMember, s_stringBuilderPool.ToStringAndReturn(sb), pattern);
                            return;
                        }

                        c = _lexer.Consume();
                        if (c == ' ' || c == '\t')
                        {
                            AddEntry(isMember, s_stringBuilderPool.ToStringAndReturn(sb), pattern);
                            break;
                        }

                        sb.Append(c);
                    }
                }
            }

            private void AddEntry(bool isMember, string name, string pattern)
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
}
