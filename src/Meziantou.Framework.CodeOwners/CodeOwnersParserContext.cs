using System.Buffers;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners;

/// <summary>Parses a CODEOWNERS file, stopping at the first error.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct CodeOwnersParserContext
{
    private static readonly SearchValues<char> PatternSeparatorSearchValues = SearchValues.Create(" \t\r\n\\");
    private static readonly SearchValues<char> MemberSeparatorSearchValues = SearchValues.Create(" \t\r\n");

    private readonly List<CodeOwnersEntry> _entries = [];
    private readonly string _content;
    private readonly CodeOwnersDialect _dialect;
    private (CodeOwnersParseErrorKind Kind, int Index)? _error;
    private CodeOwnersSection? _currentSection;
    private int _index;

    public CodeOwnersParserContext(string content, CodeOwnersDialect dialect)
    {
        _content = content;
        _dialect = dialect;
    }

    public IReadOnlyList<CodeOwnersEntry> Parse()
    {
        while (!EndOfFile && _error is null)
        {
            ParseLine();
        }

        return AsReadOnly(_entries);
    }

    public readonly bool HasError => _error is not null;

    /// <summary>Records the first error found. Parsing stops as soon as one is set.</summary>
    private void SetError(CodeOwnersParseErrorKind kind, int index)
    {
        _error ??= (kind, index);
    }

    public readonly CodeOwnersParseError CreateError()
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

        return new CodeOwnersParseError(kind, lineNumber, Math.Max(1, errorIndex - lineStartIndex + 1));
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

        // Section. Sections are a GitLab extension: in the GitHub syntax a line starting with '[' is a
        // pattern, because '[' opens a character class.
        if (_dialect is CodeOwnersDialect.GitLab && TryParseSection(out var section))
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
            entryOwners = AsReadOnly(owners);
        }
        else if (_currentSection is { HasDefaultOwners: true })
        {
            // A pattern that declares no owner inherits the ones declared on the section header.
            // The list is shared: it is never mutated once the section is parsed.
            entryOwners = _currentSection.DefaultOwners;
        }
        else
        {
            // The pattern is explicitly left unowned
            entryOwners = [];
        }

        _entries.Add(new CodeOwnersEntry(pattern, entryOwners, _currentSection));
    }

    private bool TryParseSection([NotNullWhen(true)] out CodeOwnersSection? section)
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

            // An optional section keeps the count written in the file: '^' says no approval is
            // required, not that the header declared no count.
            section = new CodeOwnersSection(name, isOptional, requiredReviewerCount, AsReadOnly(defaultOwners));
            return true;
        }

        _index = startIndex;
        section = null;
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
            SetError(CodeOwnersParseErrorKind.UnterminatedSectionHeader, startIndex);
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
            SetError(CodeOwnersParseErrorKind.UnterminatedRequiredReviewerCount, startIndex);
            _index = startIndex;
            return 1;
        }

        var requiredReviewerCountText = remaining[..separatorIndex];
        _index += separatorIndex + 1;

        // A count of 0 is how an optional section is represented, so it cannot be allowed here: it would make
        // a section that is not prefixed by '^' report itself as optional.
        if (!int.TryParse(requiredReviewerCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredReviewerCount) || requiredReviewerCount < 1)
        {
            SetError(CodeOwnersParseErrorKind.InvalidRequiredReviewerCount, startIndex);
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
                SetError(CodeOwnersParseErrorKind.EmptyOwner, lineStartIndex + offset);
            }
            else if (defaultOwner[0] is '@')
            {
                if (ParseUsernameOrRole(defaultOwner[1..]) is { } parsedOwner)
                {
                    defaultOwners.Add(parsedOwner);
                }
                else
                {
                    SetError(CodeOwnersParseErrorKind.InvalidOwner, lineStartIndex + offset);
                }
            }
            else if (IsEmailAddress(defaultOwner))
            {
                defaultOwners.Add(CodeOwner.EmailAddress(defaultOwner));
            }
            else
            {
                SetError(CodeOwnersParseErrorKind.InvalidOwner, lineStartIndex + offset);
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
                SetError(CodeOwnersParseErrorKind.EmptyOwner, tokenStartIndex);
            }
            else if (isUsername)
            {
                if (ParseUsernameOrRole(owner) is { } parsedOwner)
                {
                    owners ??= [];
                    owners.Add(parsedOwner);
                }
                else
                {
                    SetError(CodeOwnersParseErrorKind.InvalidOwner, tokenStartIndex);
                }
            }
            else if (IsEmailAddress(owner))
            {
                owners ??= [];
                owners.Add(CodeOwner.EmailAddress(owner));
            }
            else
            {
                SetError(CodeOwnersParseErrorKind.InvalidOwner, tokenStartIndex);
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

    /// <summary>Classifies the text following a leading '@', or returns null when it identifies nobody.</summary>
    private readonly CodeOwner? ParseUsernameOrRole(string name)
    {
        if (name.Length is 0)
            return null;

        if (name[0] is '@')
        {
            // Roles are a GitLab extension, and only Developer, Maintainer and Owner exist
            var role = name[1..];
            if (_dialect is CodeOwnersDialect.GitLab && IsKnownRole(role))
                return CodeOwner.Role(role);

            return null;
        }

        // '@' is not valid inside a username on either host
        if (name.AsSpan().Contains('@'))
            return null;

        return CodeOwner.Username(name);
    }

    private static bool IsKnownRole(string role)
    {
        var singular = role.Length > 0 && role[^1] is 's' or 'S' ? role[..^1] : role;
        return singular.Equals("developer", StringComparison.OrdinalIgnoreCase)
            || singular.Equals("maintainer", StringComparison.OrdinalIgnoreCase)
            || singular.Equals("owner", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns a view that callers cannot cast back to a mutable list.</summary>
    private static ReadOnlyCollection<T> AsReadOnly<T>(List<T>? list)
        => list is null || list.Count is 0 ? ReadOnlyCollection<T>.Empty : new ReadOnlyCollection<T>(list);

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
