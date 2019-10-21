using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Meziantou.Framework
{
    // https://blogs.msdn.microsoft.com/twistylittlepassagesallalike/2011/04/23/everyone-quotes-command-line-arguments-the-wrong-way/
    public static class CommandLineBuilder
    {
        private static readonly char[] s_reservedCharacters = { ' ', '\t', '\n', '\v', '"' };
        private static readonly char[] s_cmdReservedCharacters = { '(', ')', '%', '!', '^', '"', '<', '>', '&', '|' };
        private static readonly char[] s_allReservedCharacters = s_reservedCharacters.Concat(s_cmdReservedCharacters).ToArray();

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

        [return: NotNullIfNotNull(parameterName: "value")]
        public static string? WindowsQuotedArgument(string? value)
        {
            if (value == null)
                return null;

            var sb = new StringBuilder();
            if (value.Length > 0 && value.IndexOfAny(s_reservedCharacters) < 0)
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

        [return: NotNullIfNotNull(parameterName: "value")]
        public static string? WindowsCmdArgument(string? value)
        {
            if (value == null)
                return null;

            var sb = new StringBuilder();
            if (value.Length > 0 && value.IndexOfAny(s_allReservedCharacters) < 0)
                return value;

            sb.Append('"');
            EscapeArgument(value, sb);
            sb.Append('"');

            for (var i = sb.Length - 2; i >= 1; i--)
            {
                var c = sb[i];
                if (s_cmdReservedCharacters.Contains(c))
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
}
