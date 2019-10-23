using System;
using System.Collections.Generic;
using System.Linq;

namespace Meziantou.Framework
{
    public class CommandLineParser
    {
        private static readonly string[] s_helpArguments = { "-?", "/?", "-help", "/help", "--help" };

        private readonly IDictionary<string, string> _namedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IDictionary<int, string> _positionArguments = new Dictionary<int, string>();

        public static CommandLineParser Current { get; } = ParseCurrent();

        private static CommandLineParser ParseCurrent()
        {
            var parser = new CommandLineParser();
            parser.Parse(Environment.GetCommandLineArgs());
            return parser;
        }

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

                if (arg[0] == '-' || arg[0] == '/')
                {
                    arg = arg.Substring(1);
                    var indexOfSeparator = arg.IndexOfAny(new[] { ':', '=' });

                    var name = arg;
                    var value = string.Empty;
                    if (indexOfSeparator >= 0)
                    {
                        name = arg.Substring(0, indexOfSeparator).Trim();
                        value = arg.Substring(indexOfSeparator + 1);
                    }

                    _namedArguments[name] = value;
                }

                _positionArguments[i] = arg;
            }
        }

        public bool HelpRequested { get; private set; }

        public bool HasArgument(string name)
        {
            return _namedArguments.ContainsKey(name);
        }

        public string? GetArgument(string name)
        {
            if (_namedArguments.TryGetValue(name, out var value))
                return value;

            return null;
        }

        public string? GetArgument(int position)
        {
            if (_positionArguments.TryGetValue(position, out var value))
                return value;

            return null;
        }

        private static bool IsHelpArgument(string arg)
        {
            return s_helpArguments.Contains(arg, StringComparer.OrdinalIgnoreCase);
        }
    }
}
