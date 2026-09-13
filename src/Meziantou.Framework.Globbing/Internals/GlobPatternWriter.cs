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
