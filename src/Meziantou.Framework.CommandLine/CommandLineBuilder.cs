namespace Meziantou.Framework;

/// <summary>Provides methods for properly escaping and quoting command-line arguments for Windows applications.</summary>
/// <example>
/// <code>
/// // Quote a single argument for standard Windows applications
/// var arg = CommandLineBuilder.WindowsQuotedArgument(@"path with spaces\file.txt");
/// // Returns: "path with spaces\file.txt"
///
/// // Quote multiple arguments
/// var args = CommandLineBuilder.WindowsQuotedArguments("arg1", "path with spaces", "normal");
/// // Returns: arg1 "path with spaces" normal
///
/// // Quote for cmd.exe (handles special characters like &amp;, |, ^, etc.)
/// var cmdArg = CommandLineBuilder.WindowsCmdArgument(@"malicious argument"" &amp; whoami");
/// // Returns properly escaped argument safe for cmd.exe
/// </code>
/// </example>
// https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/
#if CommandLineBuilder_PUBLIC
public
#else
internal
#endif
static class CommandLineBuilder
{
    private static readonly char[] ReservedCharacters = [' ', '\t', '\n', '\v', '"'];
    private static readonly char[] CmdReservedCharacters = ['(', ')', '%', '!', '^', '"', '<', '>', '&', '|'];
    private static readonly char[] AllReservedCharacters = [.. ReservedCharacters, .. CmdReservedCharacters];

    private static void EscapeArgument(string value, StringBuilder sb)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var numberBackslashes = 0;

            while (i < value.Length && c == '\\')
            {
                i++;
                c = value[i];
                numberBackslashes++;
            }

            if (i == value.Length)
            {
                sb.Append(new string('\\', numberBackslashes * 2));
                break;
            }
            else if (c == '"')
            {
                sb.Append(new string('\\', (numberBackslashes * 2) + 1));
                sb.Append(c);
            }
            else
            {
                sb.Append(new string('\\', numberBackslashes));
                sb.Append(c);
            }
        }
    }

    /// <summary>Quotes and escapes a command-line argument for standard Windows applications.</summary>
    /// <param name="value">The argument value to quote and escape.</param>
    /// <returns>The quoted and escaped argument string, or <see langword="null"/> if <paramref name="value"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(parameterName: nameof(value))]
    public static string? WindowsQuotedArgument(string? value)
    {
        if (value is null)
            return null;

        var sb = new StringBuilder();
        if (value.Length > 0 && value.IndexOfAny(ReservedCharacters) < 0)
            return value;

        sb.Append('"');
        EscapeArgument(value, sb);

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Quotes and escapes multiple command-line arguments for standard Windows applications and joins them with spaces.</summary>
    /// <param name="values">The argument values to quote and escape.</param>
    /// <returns>A string containing all quoted and escaped arguments joined with spaces.</returns>
    public static string WindowsQuotedArguments(params string[] values)
    {
        return string.Join(' ', values.Select(WindowsQuotedArgument));
    }

    /// <summary>Quotes and escapes a command-line argument for cmd.exe, handling special characters like (, ), %, !, ^, ", &lt;, &gt;, &amp;, and |.</summary>
    /// <param name="value">The argument value to quote and escape.</param>
    /// <returns>The quoted and escaped argument string safe for cmd.exe, or <see langword="null"/> if <paramref name="value"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(parameterName: nameof(value))]
    public static string? WindowsCmdArgument(string? value)
    {
        if (value is null)
            return null;

        var sb = new StringBuilder();
        if (value.Length > 0 && value.IndexOfAny(AllReservedCharacters) < 0)
            return value;

        sb.Append('"');
        EscapeArgument(value, sb);
        sb.Append('"');

        for (var i = sb.Length - 1; i >= 0; i--)
        {
            var c = sb[i];
            if (CmdReservedCharacters.Contains(c))
            {
                sb.Insert(i, '^');
            }
        }

        return sb.ToString();
    }

    /// <summary>Quotes and escapes multiple command-line arguments for cmd.exe and joins them with spaces.</summary>
    /// <param name="values">The argument values to quote and escape.</param>
    /// <returns>A string containing all quoted and escaped arguments joined with spaces, safe for cmd.exe execution.</returns>
    public static string WindowsCmdArguments(params string[] values)
    {
        return string.Join(' ', values.Select(WindowsCmdArgument));
    }
}
