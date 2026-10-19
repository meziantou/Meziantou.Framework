using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Meziantou.Framework.Globbing;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.StaticFiles;
using WebPlatformHtmlFormatter = Meziantou.Framework.WebPlatform.Formatting.HtmlFormatter;

[assembly: InternalsVisibleTo("Meziantou.Framework.Html.Tool.Tests")]

namespace Meziantou.Framework.Html.Tool;

internal static partial class Program
{
    static Program()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var rootCommand = new RootCommand();
        AddReplaceValueCommand(rootCommand);
        AddAppendVersionCommand(rootCommand);
        InlineResourceCommand(rootCommand);
        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
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
            return ReplaceValue(
                parseResult.GetValue(singleFileOption),
                parseResult.GetValue(filePatternOption),
                parseResult.GetValue(rootDirectoryOption),
                parseResult.GetRequiredValue(xpathOption),
                parseResult.GetRequiredValue(newValueOption),
                cancellationToken);
        });

        rootCommand.Subcommands.Add(replaceValueCommand);
    }

    private static async Task<int> ReplaceValue(string? filePath, string? globPattern, string? rootDirectory, string xpath, string newValue, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            await UpdateFileAsync(filePath, xpath, newValue, cancellationToken);
        }

        if (!string.IsNullOrEmpty(globPattern))
        {
            if (!Glob.TryParse(globPattern, GlobDialect.Standard, GlobOptions.None, out var glob))
            {
                await Console.Error.WriteLineAsync($"Glob pattern '{globPattern}' is invalid".AsMemory(), cancellationToken);
                return -1;
            }

            foreach (var file in glob.EnumerateFiles(string.IsNullOrEmpty(rootDirectory) ? Environment.CurrentDirectory : rootDirectory))
            {
                await UpdateFileAsync(file, xpath, newValue, cancellationToken);
            }
        }

        return 0;

        static async Task UpdateFileAsync(string file, string xpath, string newValue, CancellationToken cancellationToken)
        {
            var htmlFile = await LoadHtmlFileAsync(file, cancellationToken);

            var count = 0;
            foreach (var node in SelectNodes(htmlFile, xpath))
            {
                node.TextContent = newValue;
                count++;
            }

            await SaveHtmlFileAsync(htmlFile, file, cancellationToken);
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
                if (!Glob.TryParse(filePattern, GlobDialect.Standard, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid".AsMemory(), cancellationToken);
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
                var htmlFile = await LoadHtmlFileAsync(file, cancellationToken).ConfigureAwait(false);

                var count = 0;
                foreach (var node in SelectNodes(htmlFile, "//@src|//@href|//@poster").OfType<Attr>())
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
                            var endIndex = uriQuery.IndexOf('&', index + 1, StringComparison.Ordinal);
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
                    await SaveHtmlFileAsync(htmlFile, file, cancellationToken).ConfigureAwait(false);
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
                if (!Glob.TryParse(filePattern, GlobDialect.Standard, GlobOptions.None, out var glob))
                {
                    await Console.Error.WriteLineAsync($"Glob pattern '{filePattern}' is invalid".AsMemory(), cancellationToken);
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
                var htmlFile = await LoadHtmlFileAsync(file, cancellationToken).ConfigureAwait(false);

                var count = 0;
                foreach (var node in SelectNodes(htmlFile, "//@src|//@href|//@poster").OfType<Attr>())
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

                    var element = node.OwnerElement;
                    if (element is not null && string.Equals(element.LocalName, "script", StringComparison.Ordinal))
                    {
                        var text = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        element.RemoveAttribute("src");
                        element.TextContent = text;
                    }
                    else if (element is not null && string.Equals(element.LocalName, "link", StringComparison.Ordinal))
                    {
                        // The DOM cannot rename an element, so the link is replaced with a new style element
                        var text = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);
                        var style = htmlFile.Document.CreateElement("style");
                        style.TextContent = text;
                        element.ReplaceWith(style);
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
                    await SaveHtmlFileAsync(htmlFile, file, cancellationToken).ConfigureAwait(false);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Updated {count} nodes in '{file}'"));
            }
        });

        rootCommand.Subcommands.Add(command);
    }

    private static async Task<HtmlFile> LoadHtmlFileAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var parser = new HtmlDocumentParser();
        var document = parser.Parse(bytes);
        var encoding = GetEncoding(document.CharacterSet);
        var preamble = encoding.Preamble;
        var hasByteOrderMark = !preamble.IsEmpty && bytes.AsSpan().StartsWith(preamble);
        var text = encoding.GetString(bytes.AsSpan(hasByteOrderMark ? preamble.Length : 0));
        if (DocumentMarkupRegex().IsMatch(text))
            return new HtmlFile(document, document, encoding, hasByteOrderMark);

        // The file does not contain the markup of a whole document, so it is parsed as a fragment to not add the missing html, head, and body elements
        var fragment = parser.ParseFragment(text, HtmlDocumentParser.ParseDocument("").Body!);
        return new HtmlFile(fragment.OwnerDocument!, fragment, encoding, hasByteOrderMark);

        static Encoding GetEncoding(string name)
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }

    private static async Task SaveHtmlFileAsync(HtmlFile file, string path, CancellationToken cancellationToken)
    {
        var encoding = file.Encoding is UTF8Encoding ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: file.HasByteOrderMark) : file.Encoding;
        await File.WriteAllTextAsync(path, WebPlatformHtmlFormatter.Default.Format(file.Root), encoding, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<Node> SelectNodes(HtmlFile file, string xpath)
    {
        var result = file.Document.Evaluate(xpath, file.Root, resolver: null, XPathResult.OrderedNodeSnapshotType);
        for (var i = 0u; i < result.SnapshotLength; i++)
        {
            yield return result.SnapshotItem(i)!;
        }
    }

    [GeneratedRegex(@"<(?:!doctype|html|head|body)[\s/>]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex DocumentMarkupRegex();

    private sealed record HtmlFile(Document Document, Node Root, Encoding Encoding, bool HasByteOrderMark);
}
