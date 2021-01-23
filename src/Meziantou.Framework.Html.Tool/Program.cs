using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.Html.Tool
{
    internal static class Program
    {
        private static Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();

            var replaceValueCommand = new Command("replace-value")
            {
                new Option<Glob>(
                    "--file-pattern",
                    parseArgument: arg => Glob.Parse(arg.GetValueOrDefault<string>(), GlobOptions.None),
                    description: "Glob pattern to find files to update") { IsRequired = true },

                new Option<string>(
                    "--xpath",
                    "XPath to the elements/attributes to replace") { IsRequired = true },

                new Option<string>(
                    "--new-value",
                    "New value for the elements/attributes") { IsRequired = true },
            };

            replaceValueCommand.Description = "Replace element/attribute values in an html file";
            replaceValueCommand.Handler = CommandHandler.Create((Glob glob, string xpath, string newValue) => ReplaceValue(glob, xpath, newValue));

            rootCommand.AddCommand(replaceValueCommand);
            return rootCommand.InvokeAsync(args);
        }

        private static async Task<int> ReplaceValue(Glob glob, string xpath, string newValue)
        {
            foreach (var file in glob.EnumerateFiles(Environment.CurrentDirectory))
            {
                var stream = File.OpenRead(file);
                try
                {
                    var doc = new HtmlDocument();
                    doc.Load(stream);

                    var count = 0;
                    var nodes = doc.SelectNodes(xpath);
                    foreach (var node in nodes)
                    {
                        node.Value = newValue;
                        count++;
                    }

                    doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
                    Console.WriteLine(FormattableString.Invariant($"Updated {count} nodes in '{file}'"));
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }

            return 0;
        }
    }
}
