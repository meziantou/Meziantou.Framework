using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.Html.Tool
{
    internal static class Program
    {
        private static Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            AddReplaceValueCommand(rootCommand);
            AddAppendVersionCommand(rootCommand);
            return rootCommand.InvokeAsync(args);
        }

        private static void AddReplaceValueCommand(RootCommand rootCommand)
        {
            var replaceValueCommand = new Command("replace-value")
            {
                new Option<string>(
                    "--file",
                    description: "Path of the file to update") { IsRequired = false },

                new Option<string>(
                    "--file-pattern",
                    description: "Glob pattern to find files to update") { IsRequired = false },

                new Option<string>(
                    "--xpath",
                    "XPath to the elements/attributes to replace") { IsRequired = true },

                new Option<string>(
                    "--new-value",
                    "New value for the elements/attributes") { IsRequired = true },
            };

            replaceValueCommand.Description = "Replace element/attribute values in an html file";
            replaceValueCommand.Handler = CommandHandler.Create((string? file, string? filePattern, string xpath, string newValue) => ReplaceValue(file, filePattern, xpath, newValue));

            rootCommand.AddCommand(replaceValueCommand);
        }

        private static async Task<int> ReplaceValue(string? filePath, string? globPattern, string xpath, string newValue)
        {
            if (filePath != null)
            {
                await UpdateFileAsync(filePath, xpath, newValue);
            }

            if (globPattern != null)
            {
                if (!Glob.TryParse(globPattern, GlobOptions.None, out var glob))
                {
                    Console.Error.WriteLine($"Glob pattern '{globPattern}' is invalid");
                    return -1;
                }

                foreach (var file in glob.EnumerateFiles(Environment.CurrentDirectory))
                {
                    await UpdateFileAsync(file, xpath, newValue);
                }
            }

            return 0;

            static async Task UpdateFileAsync(string file, string xpath, string newValue)
            {
                var doc = new HtmlDocument();
                await using (var stream = File.OpenRead(file))
                {
                    doc.Load(stream);
                }

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
        }

        private static void AddAppendVersionCommand(RootCommand rootCommand)
        {
            var command = new Command("append-version")
            {
                new Option<string>(
                    "--file",
                    description: "Path of the file to update") { IsRequired = false },

                new Option<string>(
                    "--file-pattern",
                    description: "Glob pattern to find files to update") { IsRequired = false },
            };

            command.Description = "Append version to style / script URLs";
            command.Handler = CommandHandler.Create(async (string? file, string? filePattern) =>
            {
                if (file != null)
                {
                    await UpdateFileAsync(file).ConfigureAwait(false);
                }

                if (filePattern != null)
                {
                    if (!Glob.TryParse(filePattern, GlobOptions.None, out var glob))
                    {
                        Console.Error.WriteLine($"Glob pattern '{filePattern}' is invalid");
                        return -1;
                    }

                    foreach (var f in glob.EnumerateFiles(Environment.CurrentDirectory))
                    {
                        await UpdateFileAsync(f).ConfigureAwait(false);
                    }
                }

                return 0;

                static async Task UpdateFileAsync(string file)
                {
                    var doc = new HtmlDocument();
                    await using (var stream = File.OpenRead(file))
                    {
                        doc.Load(stream);
                    }

                    var count = 0;
                    var nodes = doc.SelectNodes("//@src|//@href");
                    foreach (var node in nodes)
                    {
                        if (string.IsNullOrWhiteSpace(node.Value))
                            continue;

                        // Only consider relative path
                        if (node.Value.Contains("://", StringComparison.Ordinal) && node.Value.StartsWith("//", StringComparison.Ordinal))
                            continue;

                        string? uriPath = null;
                        string? uriQuery = null;
                        string? uriHash = null;

                        var hashIndex = node.Value.IndexOf('#', StringComparison.Ordinal);
                        var queryIndex = node.Value.IndexOf('?', StringComparison.Ordinal);
                        if (hashIndex >= 0 && queryIndex > hashIndex)
                        {
                            queryIndex = -1;
                        }

                        if (queryIndex >= 0 || hashIndex >= 0)
                        {
                            uriPath = node.Value[0..(queryIndex >= 0 ? queryIndex : hashIndex)];

                            if (queryIndex >= 0)
                            {
                                if (hashIndex >= 0)
                                {
                                    uriQuery = node.Value[queryIndex..hashIndex];
                                }
                                else
                                {
                                    uriQuery = node.Value[queryIndex..];
                                }
                            }

                            if (hashIndex >= 0)
                            {
                                uriHash = node.Value[hashIndex..];
                            }
                        }
                        else
                        {
                            uriPath = node.Value;
                        }

                        var assetPath = Path.Combine(Path.GetDirectoryName(file), uriPath);
                        if (!File.Exists(assetPath))
                            continue;

                        var bytes = await File.ReadAllBytesAsync(assetPath).ConfigureAwait(false);
#pragma warning disable CA1308 // Normalize strings to uppercase
                        var hash = Convert.ToHexString(SHA512.HashData(bytes))[0..6].ToLowerInvariant();
#pragma warning restore CA1308

                        if (uriQuery is null)
                        {
                            uriQuery = "?v=" + hash;
                        }
                        else
                        {
                            var index = uriQuery.IndexOf("&v=", StringComparison.Ordinal);
                            if (index < 0)
                            {
                                index = uriQuery.IndexOf("?v=", StringComparison.Ordinal);
                            }

                            if (index >= 0)
                            {
                                var endIndex = uriQuery.IndexOf('&', index + 1);
                                if (endIndex < 0)
                                {
                                    uriQuery = uriQuery[0..index] + (index == 0 ? '?' : '&') + "v=" + hash;
                                }
                                else
                                {
                                    uriQuery = uriQuery[0..index] + (index == 0 ? '?' : '&') + "v=" + hash + uriQuery[endIndex..];
                                }
                            }
                            else
                            {
                                uriQuery += "&v=" + hash;
                            }
                        }

                        node.Value = uriPath + uriQuery + uriHash;
                        count++;
                    }

                    if (count > 0)
                    {
                        doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
                    }

                    Console.WriteLine(FormattableString.Invariant($"Updated {count} nodes in '{file}'"));
                }
            });

            rootCommand.AddCommand(command);
        }
    }
}
