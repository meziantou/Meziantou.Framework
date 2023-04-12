using System.Text;

namespace Meziantou.Framework;

// https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/
#if CommandLineBuilder_PUBLIC
public
#else
internal
#endif
static class CommandLineBuilder
{
    private static readonly char[] ReservedCharacters = { ' ', '\t', '\n', '\v', '"' };
    private static readonly char[] CmdReservedCharacters = { '(', ')', '%', '!', '^', '"', '<', '>', '&', '|' };
    private static readonly char[] AllReservedCharacters = ReservedCharacters.Concat(CmdReservedCharacters).ToArray();

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

    [return: NotNullIfNotNull(parameterName: nameof(value))]
    public static string? WindowsQuotedArgument(string? value)
    {
        if (value == null)
            return null;

        var sb = new StringBuilder();
        if (value.Length > 0 && value.IndexOfAny(ReservedCharacters) < 0)
            return value;

        sb.Append('"');
        EscapeArgument(value, sb);

        sb.Append('"');
        return sb.ToString();
    }

    public static string WindowsQuotedArguments(params string[] values)
    {
        return string.Join(" ", values.Select(WindowsQuotedArgument));
    }

    [return: NotNullIfNotNull(parameterName: nameof(value))]
    public static string? WindowsCmdArgument(string? value)
    {
        if (value == null)
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

    public static string WindowsCmdArguments(params string[] values)
    {
        return string.Join(" ", values.Select(WindowsCmdArgument));
    }
}
