namespace Meziantou.Framework.Globbing.Internals;

/// <summary>Writes literal text into a pattern, escaping the characters that the dialect would read as syntax.</summary>
internal static class GlobPatternWriter
{
    public static void AppendLiteral(ref ValueStringBuilder sb, ReadOnlySpan<char> value, GlobDialect dialect)
    {
        foreach (var c in value)
        {
            AppendLiteral(ref sb, c, dialect);
        }
    }

    /// <summary>
    ///     Writes a literal that makes a whole path segment. <see cref="GlobDialect.Standard"/> and
    ///     <see cref="GlobDialect.MSBuild"/> read a '.' or '..' segment as a relative path, so its first dot is escaped.
    /// </summary>
    public static void AppendPathSegmentLiteral(ref ValueStringBuilder sb, string value, GlobDialect dialect)
    {
        if (value is "." or ".." && dialect is GlobDialect.Standard or GlobDialect.MSBuild)
        {
            AppendEscaped(ref sb, '.', dialect);
            AppendLiteral(ref sb, value.AsSpan(1), dialect);
            return;
        }

        AppendLiteral(ref sb, value, dialect);
    }

    /// <summary>
    ///     Writes text as a pattern that matches exactly this text. A path separator stays a separator, so each path
    ///     segment is written as a literal segment. The output can be embedded in a larger pattern.
    /// </summary>
    public static void AppendEscapedText(ref ValueStringBuilder sb, ReadOnlySpan<char> text, GlobDialect dialect)
    {
        if (text.IsEmpty)
            return;

        // A leading '!' negates the pattern, and a gitignore line starting with '#' is a comment
        if ((text[0] is '!' && dialect is GlobDialect.Standard or GlobDialect.Git) || (text[0] is '#' && dialect is GlobDialect.Git))
        {
            sb.Append('\\');
            sb.Append(text[0]);
            text = text[1..];
        }

        while (true)
        {
            var separatorIndex = IndexOfSeparator(text, dialect);
            var segment = separatorIndex < 0 ? text : text[..separatorIndex];
            AppendEscapedSegment(ref sb, segment, dialect);
            if (separatorIndex < 0)
                break;

            // MSBuild reads both '/' and '\' as separators, so the original one is kept
            sb.Append(dialect is GlobDialect.MSBuild ? text[separatorIndex] : '/');
            text = text[(separatorIndex + 1)..];
        }

        // gitignore drops the trailing spaces of a line, unless the last one is escaped
        if (dialect is GlobDialect.Git && sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Insert(sb.Length - 1, "\\");
        }

        static int IndexOfSeparator(ReadOnlySpan<char> text, GlobDialect dialect)
        {
            return dialect switch
            {
                // fnmatch without FNM_PATHNAME matches a plain string: a '/' is an ordinary character
                GlobDialect.Posix => -1,
                GlobDialect.MSBuild => text.IndexOfAny('/', '\\'),

                // On Windows, a '\' separates path segments too
                _ => text.IndexOfAny('/', Path.DirectorySeparatorChar),
            };
        }

        static void AppendEscapedSegment(ref ValueStringBuilder sb, ReadOnlySpan<char> segment, GlobDialect dialect)
        {
            if (segment is "." or ".." && dialect is GlobDialect.Standard or GlobDialect.MSBuild)
            {
                AppendEscaped(ref sb, '.', dialect);
                segment = segment[1..];
            }

            foreach (var c in segment)
            {
                // These characters have a meaning in an MSBuild item, so they are escaped too for the pattern to be
                // usable in a project file
                if (dialect is GlobDialect.MSBuild && c is ';' or '$' or '@' or '\'' or '(' or ')')
                {
                    AppendEscaped(ref sb, c, dialect);
                }
                else
                {
                    AppendLiteral(ref sb, c, dialect);
                }
            }
        }
    }

    /// <summary>Writes an alternative of a <see cref="GlobDialect.Standard"/> literal set, such as <c>{a,b}</c>.</summary>
    public static void AppendLiteralSetValue(ref ValueStringBuilder sb, string value)
    {
        foreach (var c in value)
        {
            if (c is '\\' or ',' or '}')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }
    }

    public static void AppendLiteral(ref ValueStringBuilder sb, char c, GlobDialect dialect)
    {
        var mustEscape = dialect switch
        {
            GlobDialect.MSBuild => c is '*' or '?' or '%',
            GlobDialect.Standard => c is '\\' or '*' or '?' or '[' or '{',
            _ => c is '\\' or '*' or '?' or '[',
        };

        if (mustEscape)
        {
            AppendEscaped(ref sb, c, dialect);
        }
        else
        {
            sb.Append(c);
        }
    }

    private static void AppendEscaped(ref ValueStringBuilder sb, char c, GlobDialect dialect)
    {
        if (dialect is GlobDialect.MSBuild)
        {
            // Only ASCII characters are ever escaped, so two hexadecimal digits are enough
            const string HexDigits = "0123456789ABCDEF";
            sb.Append('%');
            sb.Append(HexDigits[c >> 4]);
            sb.Append(HexDigits[c & 0xF]);
        }
        else
        {
            sb.Append('\\');
            sb.Append(c);
        }
    }
}
