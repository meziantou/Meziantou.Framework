using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Meziantou.Framework.Globbing;
using Microsoft.AspNetCore.StaticFiles;

[assembly: InternalsVisibleTo("Meziantou.Framework.Html.Tool.Tests")]

namespace Meziantou.Framework.Html.Tool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<CommandLineConfiguration>? configure)
    {
        var rootCommand = new RootCommand();
        AddReplaceValueCommand(rootCommand);
        AddAppendVersionCommand(rootCommand);
        InlineResourceCommand(rootCommand);
        var commandLineConfiguration = new CommandLineConfiguration(rootCommand);
        configure?.Invoke(commandLineConfiguration);
        return commandLineConfiguration.InvokeAsync(args);
    }

    private static void AddReplaceValueCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file") { Required = false, Description = "Path of the file to update" };
        var filePatternOption = new Option<string>("--file-pattern") { Required = false, Description = "Glob pattern to find files to update" };
        var rootDirectoryOption = new Option<string>("--root-directory") { Required = false, Description = "Root directory for glob pattern" };
        var xpathOption = new Option<string>("--xpath") { Required = true, Description = "XPath to the elements/attributes to replace" };
        var newValueOption = new Option<string>("--new-value") { Required = true, Description = "New value for the elements/attributes" };

        var replaceValueCommand = new Command("replace-value")
        {
            Description = "Replace element/attribute values in an html file",
        };
        replaceValueCommand.Options.Add(singleFileOption);
        replaceValueCommand.Options.Add(filePatternOption);
        replaceValueCommand.Options.Add(rootDirectoryOption);
        replaceValueCommand.Options.Add(xpathOption);
        replaceValueCommand.Options.Add(newValueOption);

        replaceValueCommand.SetAction((parseResult, cancellationToken) =>
        {
            return ReplaceValue(parseResult.GetValue(singleFileOption), parseResult.GetValue(filePatternOption), parseResult.GetValue(rootDirectoryOption), parseResult.GetValue(xpathOption), parseResult.GetValue(newValueOption));
        });

        rootCommand.Subcommands.Add(replaceValueCommand);
    }

    private static async Task<int> ReplaceValue(string? filePath, string? globPattern, string? rootDirectory, string xpath, string newValue)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            await UpdateFileAsync(filePath, xpath, newValue);
        }

        if (!string.IsNullOrEmpty(globPattern))
        {
            if (!Glob.TryParse(globPattern, GlobOptions.None, out var glob))
            {
                await Console.Error.WriteLineAsync($"Glob pattern '{globPattern}' is invalid");
                return -1;
            }

            foreach (var file in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
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
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
        }
    }

    private static void AddAppendVersionCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file") { Required = false, Description = "Path of the file to update" };
        var filePatternOption = new Option<string>("--file-pattern") { Required = false, Description = "Glob pattern to find files to update" };
        var rootDirectoryOption = new Option<string>("--root-directory") { Required = false, Description = "Root directory for glob pattern" };

        var command = new Command("append-version")
        {
            Description = "Append version to style / script URLs",
        };
        command.Options.Add(singleFileOption);
        command.Options.Add(filePatternOption);
        command.Options.Add(rootDirectoryOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var singleFile = parseResult.GetValue(singleFileOption);
            var filePattern = parseResult.GetValue(filePatternOption);
            var rootDirectory = parseResult.GetValue(rootDirectoryOption);
            if (!string.IsNullOrEmpty(singleFile))
            {
                await UpdateFileAsync(singleFile, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(filePattern))
            {
                if (!Glob.TryParse(filePattern, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid");
                    return -1;
                }

                foreach (var f in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
                {
                    await UpdateFileAsync(f, cancellationToken).ConfigureAwait(false);
                }
            }

            return 0;

            static async Task UpdateFileAsync(string file, CancellationToken cancellationToken)
            {
                var doc = new HtmlDocument();
                await using (var stream = File.OpenRead(file))
                {
                    doc.Load(stream);
                }

                var count = 0;
                var nodes = doc.SelectNodes("//@src|//@href|//@poster");
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

                    var parentFolder = Path.GetDirectoryName(file);
                    var assetPath = parentFolder is not null ? Path.Combine(parentFolder, uriPath) : uriPath;
                    if (!File.Exists(assetPath))
                        continue;

                    var bytes = await File.ReadAllBytesAsync(assetPath, cancellationToken).ConfigureAwait(false);
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

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
            }
        });

        rootCommand.Subcommands.Add(command);
    }

    private static void InlineResourceCommand(RootCommand rootCommand)
    {
        var singleFileOption = new Option<string>("--single-file") { Required = false, Description = "Path of the file to update" };
        var filePatternOption = new Option<string>("--file-pattern") { Required = false, Description = "Glob pattern to find files to update" };
        var rootDirectoryOption = new Option<string>("--root-directory") { Required = false, Description = "Root directory for glob pattern" };
        var resourcePatternsOption = new Option<string[]>("--resource-patterns") { Required = true, AllowMultipleArgumentsPerToken = true, Arity = ArgumentArity.OneOrMore, Description = "Files to inline" };

        var command = new Command("inline-resources")
        {
            Description = "Inline scripts, styles, and images",
        };
        command.Options.Add(singleFileOption);
        command.Options.Add(filePatternOption);
        command.Options.Add(rootDirectoryOption);
        command.Options.Add(resourcePatternsOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var singleFile = parseResult.GetValue(singleFileOption);
            var filePattern = parseResult.GetValue(filePatternOption);
            var rootDirectory = parseResult.GetValue(rootDirectoryOption);
            var resourcePatterns = parseResult.GetValue(resourcePatternsOption);
            if (!string.IsNullOrEmpty(singleFile))
            {
                await UpdateFileAsync(singleFile, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(filePattern))
            {
                if (!Glob.TryParse(filePattern, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid");
                    return -1;
                }

                foreach (var f in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
                {
                    await UpdateFileAsync(f, cancellationToken).ConfigureAwait(false);
                }
            }

            return 0;

            static async Task UpdateFileAsync(string file, CancellationToken cancellationToken)
            {
                var doc = new HtmlDocument();
                await using (var stream = File.OpenRead(file))
                {
                    doc.Load(stream);
                }

                var count = 0;
                var nodes = doc.SelectNodes("//@src|//@href|//@poster");
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

                    var parentFolder = Path.GetDirectoryName(file);
                    var assetPath = parentFolder is not null ? Path.Combine(parentFolder, uriPath) : uriPath;
                    if (!File.Exists(assetPath))
                        continue;

                    var element = node.ParentElement!;
                    if (string.Equals(element.Name, "SCRIPT", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        element.RemoveAttribute("src");
                        element.InnerText = text;

                    }
                    else if (string.Equals(element.Name, "LINK", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        element.Name = "style";
                        element.RemoveAttribute("href");
                        element.InnerText = text;
                    }
                    else
                    {
                        var bytes = await File.ReadAllBytesAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        var base64 = Convert.ToBase64String(bytes);
                        if (!new FileExtensionContentTypeProvider().TryGetContentType(assetPath, out var contentType))
                        {
                            contentType = "application/octet-stream";
                        }

                        var srcAttribute = $"data:{contentType};base64,{base64}";
                        node.Value = srcAttribute;
                    }

                    count++;
                }

                if (count > 0)
                {
                    doc.Save(file, doc.DetectedEncoding ?? doc.StreamEncoding);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
            }
        });

        rootCommand.Subcommands.Add(command);
    }
}
