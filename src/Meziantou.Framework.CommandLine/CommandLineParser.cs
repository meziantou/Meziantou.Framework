namespace Meziantou.Framework;

/// <summary>Parses command-line arguments with support for named and positional arguments.</summary>
/// <example>
/// <code>
/// // Parse current process arguments
/// var parser = CommandLineParser.Current;
///
/// // Or parse custom arguments
/// var customParser = new CommandLineParser();
/// customParser.Parse(new[] { "/name=John", "/verbose", "input.txt" });
///
/// // Check if an argument exists
/// if (parser.HasArgument("verbose"))
/// {
///     Console.WriteLine("Verbose mode enabled");
/// }
///
/// // Get named argument value
/// var name = parser.GetArgument("name"); // Returns "John"
///
/// // Get positional argument
/// var inputFile = parser.GetArgument(2); // Returns "input.txt"
///
/// // Check if help was requested (detects -?, /?, -help, /help, --help)
/// if (parser.HelpRequested)
/// {
///     ShowHelp();
/// }
/// </code>
/// </example>
public sealed class CommandLineParser
{
    private static readonly string[] HelpArguments = ["-?", "/?", "-help", "/help", "--help"];

    private static readonly char[] ValueDelimiters = [':', '='];

    private readonly Dictionary<string, string> _namedArguments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _positionArguments = [];

    /// <summary>Gets a parser for the current process's command-line arguments.</summary>
    public static CommandLineParser Current { get; } = ParseCurrent();

    private static CommandLineParser ParseCurrent()
    {
        var parser = new CommandLineParser();
        parser.Parse(Environment.GetCommandLineArgs());
        return parser;
    }

    /// <summary>Parses the specified command-line arguments. Named arguments use / or - prefix with : or = separator. Help arguments (-?, /?, -help, /help, --help) set <see cref="HelpRequested"/>.</summary>
    /// <param name="args">The command-line arguments to parse.</param>
    public void Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg))
                continue;

            if (arg.All(c => c == ' '))
            {
                _positionArguments[i] = arg;
                continue;
            }

            arg = arg.TrimStart();
            if (string.IsNullOrEmpty(arg))
                continue;

            if (IsHelpArgument(arg))
            {
                HelpRequested = true;
                continue;
            }

            if (arg[0] is '-' or '/')
            {
                arg = arg[1..];
                var indexOfSeparator = arg.IndexOfAny(ValueDelimiters);

                var name = arg;
                var value = "";
                if (indexOfSeparator >= 0)
                {
                    name = arg[..indexOfSeparator].Trim();
                    value = arg[(indexOfSeparator + 1)..];
                }

                _namedArguments[name] = value;
            }

            _positionArguments[i] = arg;
        }
    }

    /// <summary>Gets a value indicating whether help was requested through any of the standard help arguments (-?, /?, -help, /help, --help).</summary>
    public bool HelpRequested { get; private set; }

    /// <summary>Determines whether the specified named argument exists.</summary>
    /// <param name="name">The name of the argument to check.</param>
    /// <returns><see langword="true"/> if the argument exists; otherwise, <see langword="false"/>.</returns>
    public bool HasArgument(string name)
    {
        return _namedArguments.ContainsKey(name);
    }

    /// <summary>Gets the value of the specified named argument.</summary>
    /// <param name="name">The name of the argument.</param>
    /// <returns>The argument value, or <see langword="null"/> if the argument does not exist.</returns>
    public string? GetArgument(string name)
    {
        if (_namedArguments.TryGetValue(name, out var value))
            return value;

        return null;
    }

    /// <summary>Gets the argument at the specified position.</summary>
    /// <param name="position">The zero-based position of the argument.</param>
    /// <returns>The argument at the specified position, or <see langword="null"/> if no argument exists at that position.</returns>
    public string? GetArgument(int position)
    {
        if (_positionArguments.TryGetValue(position, out var value))
            return value;

        return null;
    }

    private static bool IsHelpArgument(string arg)
    {
        return HelpArguments.Contains(arg, StringComparer.OrdinalIgnoreCase);
    }
}
