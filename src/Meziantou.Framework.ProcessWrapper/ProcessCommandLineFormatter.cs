using System.Text;

namespace Meziantou.Framework;

internal static class ProcessCommandLineFormatter
{
    public static string Format(string processFileName, IEnumerable<string> arguments)
    {
        return Format(processFileName, arguments, ProcessLogVerbosity.IncludeProcessPath | ProcessLogVerbosity.IncludeArguments);
    }

    public static string Format(string processFileName, IEnumerable<string> arguments, ProcessLogVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(processFileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var includeProcessPath = (verbosity & ProcessLogVerbosity.IncludeProcessPath) == ProcessLogVerbosity.IncludeProcessPath;
        var includeArguments = (verbosity & ProcessLogVerbosity.IncludeArguments) == ProcessLogVerbosity.IncludeArguments;
        if (!includeProcessPath && !includeArguments)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (includeProcessPath)
        {
            sb.Append(CommandLineBuilder.WindowsQuotedArgument(processFileName));
        }

        if (includeArguments)
        {
            foreach (var argument in arguments)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(CommandLineBuilder.WindowsQuotedArgument(argument));
            }
        }

        return sb.ToString();
    }
}
