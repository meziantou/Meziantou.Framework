namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// Parses git config files (.git/config, .gitmodules) following the rules of git's config.c:
/// unquoted <c>#</c> and <c>;</c> start a comment, double-quoted parts can contain them, <c>\"</c> and <c>\\</c> are escapes,
/// a trailing <c>\</c> continues the value on the next line, and a section header can be followed by a comment or an entry.
/// Malformed lines are skipped instead of failing the whole file.
/// </summary>
internal static class GitConfigParser
{
    public static List<GitConfigEntry> Parse(string text)
    {
        var result = new List<GitConfigEntry>();
        var reader = new Reader(text);
        string? section = null;
        string? subsection = null;

        while (true)
        {
            var c = reader.Read();
            if (c < 0)
                break;

            if (c is '\n' || IsSpace(c))
                continue;

            if (c is '#' or ';')
            {
                reader.SkipLine();
                continue;
            }

            if (c is '[')
            {
                if (TryParseSectionHeader(ref reader, out section, out subsection))
                    continue;

                section = null;
                subsection = null;
                reader.SkipLine();
                continue;
            }

            if (!char.IsAsciiLetter((char)c))
            {
                reader.SkipLine();
                continue;
            }

            var key = new StringBuilder();
            key.Append(char.ToLowerInvariant((char)c));
            while (reader.Peek() is var next and >= 0 && IsKeyCharacter(next))
            {
                key.Append(char.ToLowerInvariant((char)reader.Read()));
            }

            while (reader.Peek() is ' ' or '\t')
            {
                reader.Read();
            }

            string? value;
            var separator = reader.Peek();
            if (separator is '\n' or < 0)
            {
                // "key" alone is a boolean true
                value = null;
            }
            else if (separator is '=')
            {
                reader.Read();
                value = ParseValue(ref reader);
            }
            else
            {
                reader.SkipLine();
                continue;
            }

            if (section is not null)
            {
                result.Add(new GitConfigEntry(section, subsection, key.ToString(), value));
            }
        }

        return result;
    }

    private static bool TryParseSectionHeader(ref Reader reader, out string? section, out string? subsection)
    {
        section = null;
        subsection = null;

        var name = new StringBuilder();
        while (true)
        {
            var c = reader.Peek();
            if (c < 0 || c is '\n')
                return false;

            if (c is ']')
            {
                reader.Read();
                break;
            }

            if (IsSpace(c))
            {
                // [section "subsection"]
                while (reader.Peek() is var space and >= 0 && space is not '\n' && IsSpace(space))
                {
                    reader.Read();
                }

                if (reader.Read() is not '"')
                    return false;

                var extendedSubsection = new StringBuilder();
                while (true)
                {
                    var subsectionCharacter = reader.Read();
                    if (subsectionCharacter < 0 || subsectionCharacter is '\n')
                        return false;

                    if (subsectionCharacter is '"')
                        break;

                    if (subsectionCharacter is '\\')
                    {
                        subsectionCharacter = reader.Read();
                        if (subsectionCharacter < 0 || subsectionCharacter is '\n')
                            return false;
                    }

                    extendedSubsection.Append((char)subsectionCharacter);
                }

                if (reader.Read() is not ']' || name.Length is 0)
                    return false;

                section = name.ToString();
                subsection = extendedSubsection.ToString();
                return true;
            }

            if (!IsKeyCharacter(c) && c is not '.')
                return false;

            name.Append(char.ToLowerInvariant((char)reader.Read()));
        }

        if (name.Length is 0)
            return false;

        // Deprecated [section.subsection] syntax: the subsection is case-insensitive
        var fullName = name.ToString();
        var dotIndex = fullName.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            section = fullName;
        }
        else
        {
            section = fullName[..dotIndex];
            subsection = fullName[(dotIndex + 1)..];
        }

        return section.Length > 0;
    }

    private static string ParseValue(ref Reader reader)
    {
        var value = new StringBuilder();
        var inQuote = false;
        var inComment = false;
        var trimLength = -1;

        while (true)
        {
            var c = reader.Peek();
            if (c < 0 || c is '\n')
                break;

            reader.Read();
            if (inComment)
                continue;

            if (IsSpace(c) && !inQuote)
            {
                if (trimLength < 0)
                {
                    trimLength = value.Length;
                }

                if (value.Length > 0)
                {
                    value.Append((char)c);
                }

                continue;
            }

            if (!inQuote && c is '#' or ';')
            {
                inComment = true;
                continue;
            }

            trimLength = -1;
            if (c is '\\')
            {
                var escaped = reader.Peek();
                switch (escaped)
                {
                    case '\n':
                        // Line continuation
                        reader.Read();
                        continue;
                    case 't':
                        reader.Read();
                        value.Append('\t');
                        continue;
                    case 'b':
                        reader.Read();
                        value.Append('\b');
                        continue;
                    case 'n':
                        reader.Read();
                        value.Append('\n');
                        continue;
                    case '\\' or '"':
                        reader.Read();
                        value.Append((char)escaped);
                        continue;
                    default:
                        // git rejects unknown escape sequences; keep them verbatim instead of dropping the entry
                        value.Append('\\');
                        continue;
                }
            }

            if (c is '"')
            {
                inQuote = !inQuote;
                continue;
            }

            value.Append((char)c);
        }

        if (trimLength >= 0)
        {
            value.Length = trimLength;
        }

        return value.ToString();
    }

    private static bool IsSpace(int c) => c is ' ' or '\t' or '\r' or '\v' or '\f';

    private static bool IsKeyCharacter(int c) => c is '-' || char.IsAsciiLetterOrDigit((char)c);

    private struct Reader
    {
        private readonly string _text;
        private int _position;

        public Reader(string text)
        {
            _text = text;
            _position = text.Length > 0 && text[0] is '﻿' ? 1 : 0;
        }

        public int Peek()
        {
            if (_position >= _text.Length)
                return -1;

            // "\r\n" is read as "\n"
            if (_text[_position] is '\r' && _position + 1 < _text.Length && _text[_position + 1] is '\n')
                return '\n';

            return _text[_position];
        }

        public int Read()
        {
            var c = Peek();
            if (c is '\n' && _text[_position] is '\r')
            {
                _position += 2;
            }
            else if (c >= 0)
            {
                _position++;
            }

            return c;
        }

        public void SkipLine()
        {
            while (Read() is var c and >= 0 && c is not '\n')
            {
            }
        }
    }
}
