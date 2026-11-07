using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace ChromiumDiff;

internal sealed class TreeComparisonOptions
{
    public string? Filter { get; init; }
    public string? ReportPath { get; init; }
    public bool Tests { get; init; } = true;
    public bool Pages { get; init; }
    public bool ExcludeNul { get; init; }
    public int Generate { get; init; }
    public int Seed { get; init; } = 1;
    public bool Documents { get; init; } = true;
    public bool Fragments { get; init; } = true;
    public HashSet<string>? Contexts { get; init; }
    public int Parallelism { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    public string Browser { get; init; } = "chromium";
    public bool Minimize { get; init; }
    public List<string> CorpusDirectories { get; } = [];
}

/// <summary>A way to parse an input: as a document, or as a fragment with a context element, with or without scripting.</summary>
internal sealed record ComparisonMode(bool Scripting, FragmentContext? Context)
{
    public string Name => (Context is null ? "document" : "fragment(" + Context.DisplayName + ")") + (Scripting ? " scripting" : "");
}

/// <summary>
/// A known difference of known-differences.json: the deviation, the comparisons it explains ("id", "id mode" or
/// "id mode check"), and minimal inputs that reproduce it.
/// </summary>
internal sealed record KnownDifference(string Reason, string[]? Differences = null, Reproduction[]? Reproductions = null, MinimizedPattern[]? Patterns = null);

/// <summary>
/// A minimal input that reproduces a known difference in the modes whose name starts with <see cref="Mode"/> (e.g.
/// "document", "fragment(select)", "fragment(svg "). The reproductions are compared with the tree construction tests, and
/// the differences of the other inputs that are minimized (--minimize) to a reproduction are known.
/// </summary>
internal sealed record Reproduction(string Mode, string Input);

/// <summary>
/// A regular expression that matches the whole minimized input (--minimize) of a known difference, in the modes whose name
/// starts with <see cref="Mode"/>. Each part of a minimized input is required for the difference, so a minimized input
/// that has the construct of a deviation (e.g. a search element) has that deviation.
/// </summary>
internal sealed record MinimizedPattern(string Mode, string Pattern)
{
    public bool IsMatch(ComparisonMode mode, string input)
        => mode.Name.StartsWith(Mode, StringComparison.Ordinal) && Regex.IsMatch(input, "^(?:" + Pattern + ")$", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}

/// <summary>
/// Compares the parsing of each input by Chromium and by the library, in each mode: as a document and as a fragment (the
/// innerHTML setter) in each context element of <see cref="FragmentContext.All"/>, with the scripting disabled and
/// enabled. Each comparison checks the trees (html5lib format), then the serializations (getHTML), then the trees parsed
/// from the serializations (the round trip). The differences that are accepted are listed in known-differences.json.
/// </summary>
internal static class TreeComparison
{
    private static readonly string[] Checks = ["tree", "serialization", "round trip"];

    // The fragments of a batch are parsed by one call to Chromium
    private const int MaxBatchLength = 20_000;

    public static async Task<int> RunAsync(string root, TreeComparisonOptions options)
    {
        var knownDifferencesPath = Path.Combine(root, "tools", "Meziantou.Framework.WebPlatform.ChromiumDiff", "known-differences.json");
        var knownDifferences = JsonSerializer.Deserialize<KnownDifference[]>(File.ReadAllText(knownDifferencesPath), new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }) ?? [];

        // The reproductions of the same input (in different modes) are compared once
        var reproductions = new Dictionary<string, List<Reproduction>>(StringComparer.Ordinal);
        if (options.Tests)
        {
            foreach (var reproduction in knownDifferences.SelectMany(known => known.Reproductions ?? []))
            {
                var id = "reproduction:" + JsonSerializer.Serialize(reproduction.Input);
                if (!reproductions.TryGetValue(id, out var list))
                {
                    reproductions[id] = list = [];
                }

                list.Add(reproduction);
            }
        }

        var inputs = Corpus.Load(root, options.Tests, options.Pages, options.CorpusDirectories)
            .Concat(reproductions.Select(r => new Input(r.Key, r.Value[0].Input)))
            .Concat(InputGenerator.Generate(options.Generate, options.Seed))
            .Where(input => options.Filter is null || input.Id.Contains(options.Filter, StringComparison.Ordinal))
            .Where(input => !options.ExcludeNul || !input.Html.Contains('\0', StringComparison.Ordinal))
            .ToList();
        var contexts = FragmentContext.All.Where(context => options.Contexts is null || options.Contexts.Contains(context.DisplayName)).ToList();
        Console.WriteLine($"{inputs.Count} input(s), {contexts.Count} context element(s)");

        using var playwright = await Playwright.CreateAsync();
        await using var session = await BrowserSession.CreateAsync(playwright, options.Browser, options.Parallelism);
        Console.WriteLine($"{options.Browser} {session.Version}");

        var differences = new ConcurrentBag<Difference>();
        var compared = new ConcurrentBag<string>();
        var stopwatch = Stopwatch.StartNew();
        foreach (var scripting in (bool[])[false, true])
        {
            if (options.Documents)
            {
                var mode = new ComparisonMode(scripting, Context: null);
                await Parallel.ForEachAsync(inputs, new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism }, async (input, cancellationToken) =>
                {
                    var chromium = await session.ParseDocumentsAsync(scripting, [input.Html]);
                    compared.Add(input.Id + " " + mode.Name);
                    Compare(differences, input, mode, chromium[0]);
                });

                Console.WriteLine($"Compared the documents{(scripting ? " with scripting" : "")} in {stopwatch.Elapsed.TotalSeconds:F1} s");
            }

            // The scripting of a quirks-mode context is always disabled
            var fragmentContexts = contexts.Where(context => !scripting || !context.Quirks).ToArray();
            if (options.Fragments && fragmentContexts.Length > 0)
            {
                await Parallel.ForEachAsync(CreateBatches(inputs.Where(input => input.Fragments)), new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism }, async (batch, cancellationToken) =>
                {
                    var results = await session.ParseFragmentsAsync(scripting, [.. batch.Select(input => input.Html)], fragmentContexts);
                    var index = 0;
                    foreach (var input in batch)
                    {
                        foreach (var context in fragmentContexts)
                        {
                            var mode = new ComparisonMode(scripting, context);
                            compared.Add(input.Id + " " + mode.Name);
                            Compare(differences, input, mode, results[index++]);
                        }
                    }
                });

                Console.WriteLine($"Compared the fragments{(scripting ? " with scripting" : "")} in {stopwatch.Elapsed.TotalSeconds:F1} s");
            }
        }

        // The differences of an input that are the same in several modes are reported once
        var report = new StringBuilder();
        var unexpected = differences.Where(d => !IsKnown(knownDifferences, reproductions, d)).ToList();
        var minimized = new List<(string Html, string Mode, string Check, string Id)>();
        var explained = 0;
        foreach (var group in unexpected.GroupBy(d => (d.Input.Id, d.Check, d.Diff)).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.Check, StringComparer.Ordinal).ToList())
        {
            var modes = group.Select(d => d.Mode).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
            var html = group.First().Input.Html;
            var entry = new StringBuilder();
            entry.Append("UNEXPECTED DIFFERENCE ").Append(group.Key.Id).Append(' ').Append(group.Key.Check).Append(" (").Append(modes.Count).Append(" mode(s): ")
                .AppendJoin(", ", modes.Take(8).Select(m => m.Name)).Append(modes.Count > 8 ? ", …" : "").Append(")\n");
            entry.Append("  input: ").Append(JsonSerializer.Serialize(Truncate(html))).Append('\n');
            entry.Append(group.Key.Diff);
            if (options.Minimize && group.Key.Check != "exception")
            {
                var mode = modes[0];
                var checkIndex = Array.IndexOf(Checks, group.Key.Check);
                var (minimal, diff) = await MinimizeAsync(session, mode, checkIndex, html);

                // A difference that minimizes to the reproduction or a pattern of a known difference is known
                if (knownDifferences.Any(known => (known.Reproductions ?? []).Any(r => r.Input == minimal && mode.Name.StartsWith(r.Mode, StringComparison.Ordinal)) || (known.Patterns ?? []).Any(p => p.IsMatch(mode, minimal))))
                {
                    explained += group.Count();
                    foreach (var difference in group)
                    {
                        unexpected.Remove(difference);
                    }

                    continue;
                }

                entry.Append("  minimized (").Append(mode.Name).Append("): ").Append(JsonSerializer.Serialize(minimal)).Append('\n').Append(diff);
                minimized.Add((minimal, mode.Context is null ? "document" : "fragment", group.Key.Check, group.Key.Id));
            }

            report.Append(entry).Append('\n');
        }

        if (minimized.Count > 0)
        {
            report.Append("MINIMIZED INPUTS\n");
            foreach (var group in minimized.GroupBy(m => (m.Html, m.Mode, m.Check)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Html, StringComparer.Ordinal))
            {
                report.Append("  ").Append(group.Count()).Append(' ').Append(group.Key.Mode).Append(' ').Append(group.Key.Check).Append(' ').Append(JsonSerializer.Serialize(group.Key.Html))
                    .Append(" (").AppendJoin(", ", group.Select(m => m.Id).Take(3)).Append(")\n");
            }
        }

        // A known difference that is no longer different must be removed from the list (only for the comparisons that ran)
        var comparedKeys = compared.ToList();
        var stale = knownDifferences.SelectMany(known => known.Differences ?? [])
            .Where(key => comparedKeys.Exists(comparison => Matches(comparison + " ", key)) && !differences.Any(d => Matches(d.Key, key)))
            .ToList();
        foreach (var (id, reproduction) in reproductions.SelectMany(r => r.Value.Select(reproduction => (r.Key, reproduction))))
        {
            if (comparedKeys.Exists(comparison => comparison.StartsWith(id + " " + reproduction.Mode, StringComparison.Ordinal)) && !differences.Any(d => d.Input.Id == id && d.Mode.Name.StartsWith(reproduction.Mode, StringComparison.Ordinal)))
            {
                stale.Add(reproduction.Mode + " " + JsonSerializer.Serialize(reproduction.Input));
            }
        }

        stale.Sort(StringComparer.Ordinal);
        foreach (var key in stale)
        {
            report.Append("STALE KNOWN DIFFERENCE ").Append(key).Append(" (no difference: remove it from known-differences.json)\n");
        }

        await Console.Out.WriteAsync(report);
        if (options.ReportPath is not null)
        {
            // The trees may contain lone surrogates (e.g. from a character reference)
            await File.WriteAllTextAsync(options.ReportPath, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));
        }

        Console.WriteLine($"{comparedKeys.Count} comparison(s) in {stopwatch.Elapsed.TotalSeconds:F1} s; {differences.Count} difference(s): {differences.Count - unexpected.Count} known ({explained} minimized to a reproduction), {unexpected.Count} unexpected; {stale.Count} stale known difference(s)");
        return unexpected.Count == 0 && stale.Count == 0 ? 0 : 1;
    }

    private static string Truncate(string html)
    {
        if (html.Length <= 500)
            return html;

        // Do not split a surrogate pair
        var length = char.IsHighSurrogate(html[499]) ? 499 : 500;
        return html[..length] + "…";
    }

    /// <summary>Returns whether a difference is explained by a known difference: its key, or the reproduction it compares.</summary>
    private static bool IsKnown(KnownDifference[] knownDifferences, Dictionary<string, List<Reproduction>> reproductions, Difference difference)
    {
        if (reproductions.TryGetValue(difference.Input.Id, out var list) && list.Exists(reproduction => difference.Mode.Name.StartsWith(reproduction.Mode, StringComparison.Ordinal)))
            return true;

        return knownDifferences.Any(known => (known.Differences ?? []).Any(knownKey => Matches(difference.Key, knownKey)));
    }

    // A known difference is "id", "id mode" or "id mode check": it matches the differences of all the modes and checks it does not specify
    private static bool Matches(string key, string knownKey) => key.Length == knownKey.Length ? string.Equals(key, knownKey, StringComparison.Ordinal) : key.StartsWith(knownKey + " ", StringComparison.Ordinal);

    private static IEnumerable<Input[]> CreateBatches(IEnumerable<Input> inputs)
    {
        var batch = new List<Input>();
        var length = 0;
        foreach (var input in inputs)
        {
            if (batch.Count > 0 && length + input.Html.Length > MaxBatchLength)
            {
                yield return [.. batch];
                batch.Clear();
                length = 0;
            }

            batch.Add(input);
            length += input.Html.Length + 100;
        }

        if (batch.Count > 0)
            yield return [.. batch];
    }

    private static void Compare(ConcurrentBag<Difference> differences, Input input, ComparisonMode mode, string[] chromium)
    {
        string[] webPlatform;
        try
        {
            webPlatform = Parse(mode, input.Html);
        }
        catch (Exception ex)
        {
            differences.Add(new Difference(input, mode, "exception", "  " + ex.ToString().Replace("\n", "\n  ", StringComparison.Ordinal) + "\n"));
            return;
        }

        if (FindDifference(chromium, webPlatform) is { } index)
        {
            differences.Add(new Difference(input, mode, Checks[index], Diff(chromium, webPlatform, index)));
        }
    }

    // The index of the first check whose results are different
    private static int? FindDifference(string[] chromium, string[] webPlatform)
    {
        for (var i = 0; i < Checks.Length; i++)
        {
            if (!string.Equals(chromium[i], webPlatform[i], StringComparison.Ordinal))
                return i;
        }

        return null;
    }

    private static string Diff(string[] chromium, string[] webPlatform, int index) => index == 1
        ? "  chromium:    " + JsonSerializer.Serialize(chromium[index]) + "\n  webplatform: " + JsonSerializer.Serialize(webPlatform[index]) + "\n"
        : TreeDumper.Diff(chromium[index], webPlatform[index]);

    /// <summary>
    /// Reduces the input of a difference (delta debugging): removes the tags and the texts, then the characters, while the
    /// same check is different in the same mode.
    /// </summary>
    private static async Task<(string Html, string Diff)> MinimizeAsync(BrowserSession session, ComparisonMode mode, int checkIndex, string html)
    {
        var chunks = Tokenize(html);
        chunks = await MinimizeChunksAsync(session, mode, checkIndex, chunks);
        if (string.Concat(chunks).Length <= 300)
        {
            // The characters, without splitting the surrogate pairs (a lone surrogate is replaced by U+FFFD in Chromium)
            chunks = await MinimizeChunksAsync(session, mode, checkIndex, [.. string.Concat(chunks).EnumerateRunes().Select(rune => rune.ToString())]);
        }

        // The same input in lowercase, if it has the same difference, so that the reproductions are easier to compare
        var minimal = string.Concat(chunks);
        var lowercase = minimal.ToLowerInvariant();
        if (lowercase != minimal && FindDifferenceSafe(mode, lowercase, (await session.ParseAsync(mode, [lowercase]))[0]) == checkIndex)
        {
            minimal = lowercase;
        }

        var chromium = (await session.ParseAsync(mode, [minimal]))[0];
        var webPlatform = Parse(mode, minimal);
        return (minimal, FindDifference(chromium, webPlatform) is { } index ? Diff(chromium, webPlatform, index) : "  (no difference)\n");

        static List<string> Tokenize(string html)
        {
            var result = new List<string>();
            var start = 0;
            for (var i = 0; i < html.Length; i++)
            {
                if (html[i] == '<' && i > start)
                {
                    result.Add(html[start..i]);
                    start = i;
                }
                else if (html[i] == '>')
                {
                    result.Add(html[start..(i + 1)]);
                    start = i + 1;
                }
            }

            if (start < html.Length)
            {
                result.Add(html[start..]);
            }

            return result;
        }
    }

    private static async Task<List<string>> MinimizeChunksAsync(BrowserSession session, ComparisonMode mode, int checkIndex, List<string> chunks)
    {
        var granularity = 2;
        while (chunks.Count >= 2)
        {
            // The complements of the parts, evaluated in one batch
            var size = (int)Math.Ceiling(chunks.Count / (double)granularity);
            var candidates = new List<List<string>>();
            for (var start = 0; start < chunks.Count; start += size)
            {
                candidates.Add([.. chunks.Take(start), .. chunks.Skip(start + size)]);
            }

            var results = await session.ParseAsync(mode, [.. candidates.Select(candidate => string.Concat(candidate))]);
            var reduced = false;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Count == 0)
                    continue;

                if (FindDifferenceSafe(mode, string.Concat(candidates[i]), results[i]) == checkIndex)
                {
                    chunks = candidates[i];
                    granularity = Math.Max(granularity - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (!reduced)
            {
                if (granularity >= chunks.Count)
                    break;

                granularity = Math.Min(chunks.Count, granularity * 2);
            }
        }

        return chunks;
    }

    private static int? FindDifferenceSafe(ComparisonMode mode, string html, string[] chromium)
    {
        try
        {
            return FindDifference(chromium, Parse(mode, html));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Parses an input with the library in a mode: the tree, the serialization, and the tree of the serialization.</summary>
    private static string[] Parse(ComparisonMode mode, string html)
    {
        if (mode.Context is not { } context)
        {
            var document = HtmlDocumentParser.ParseDocument(html, new HtmlParserOptions { ScriptingEnabled = mode.Scripting });
            var serialization = SerializeDocument(document);
            var roundTrip = HtmlDocumentParser.ParseDocument(serialization, new HtmlParserOptions { AllowDeclarativeShadowRoots = false });
            return [TreeDumper.Dump(document), serialization, TreeDumper.Dump(roundTrip)];
        }

        // The node document of the context element: a no-quirks document with the scripting of the mode, or a quirks-mode
        // document without scripting (as in fragments.js)
        var contextDocument = context.Quirks
            ? HtmlDocumentParser.ParseDocument("")
            : HtmlDocumentParser.ParseDocument("<!DOCTYPE html>", new HtmlParserOptions { ScriptingEnabled = mode.Scripting });
        var element = CreateContext(contextDocument, context);
        element.InnerHtml = html;
        var fragmentSerialization = element.GetHtml(new GetHtmlOptions { SerializableShadowRoots = true });
        var fragmentRoundTrip = CreateContext(contextDocument, context);
        fragmentRoundTrip.InnerHtml = fragmentSerialization;
        return [TreeDumper.Dump(GetRoot(element)), fragmentSerialization, TreeDumper.Dump(GetRoot(fragmentRoundTrip))];

        static Node GetRoot(Element element) => element is HtmlTemplateElement template ? template.Content : element;
    }

    private static Element CreateContext(Document document, FragmentContext context)
    {
        var element = document.CreateElementNS(context.Namespace, context.Name);
        foreach (var (name, value) in context.Attributes)
        {
            element.SetAttribute(name, value);
        }

        return element;
    }

    /// <summary>Serializes a document as tree.js (serializeDocument) does.</summary>
    private static string SerializeDocument(Document document)
    {
        var builder = new StringBuilder();
        for (var node = document.FirstChild; node is not null; node = node.NextSibling)
        {
            switch (node)
            {
                case DocumentType doctype:
                    builder.Append("<!DOCTYPE ").Append(doctype.Name).Append('>');
                    break;

                case Comment comment:
                    builder.Append("<!--").Append(comment.Data).Append("-->");
                    break;

                case Element element:
                    var endTag = "</" + element.LocalName + ">";
                    var tags = ((Element)element.CloneNode()).OuterHtml;
                    builder.Append(tags.AsSpan(0, tags.Length - endTag.Length)).Append(element.GetHtml(new GetHtmlOptions { SerializableShadowRoots = true })).Append(endTag);
                    break;
            }
        }

        return builder.ToString();
    }

    private sealed record Difference(Input Input, ComparisonMode Mode, string Check, string Diff)
    {
        public string Key => Input.Id + " " + Mode.Name + " " + Check;
    }

    /// <summary>
    /// The pages of the browser that parse the inputs: a pool of pages without scripting (the scripts are disabled, so the
    /// scripting flag of the parser is disabled as in the library, and noscript elements are parsed as normal elements),
    /// and a pool of pages with scripting (a Content Security Policy prevents the scripts of the inputs from running, so
    /// the tree is the result of the parser only). The documents are navigations; the fragments are parsed by the
    /// innerHTML setter in a blank no-quirks document.
    /// </summary>
    private sealed class BrowserSession : IAsyncDisposable
    {
        private const string BlankUrl = "https://chromium-diff.test/blank";

        private readonly IBrowser _browser;
        private readonly ConcurrentDictionary<string, byte[]> _documents = new(StringComparer.Ordinal);
        private readonly Channel[] _pages = new Channel[2];
        private readonly string _documentScript = CreateScript("document.js");
        private readonly string _fragmentScript = CreateScript("fragments.js");
        private int _counter;

        private BrowserSession(IBrowser browser) => _browser = browser;

        public string Version => _browser.Version;

        public static async Task<BrowserSession> CreateAsync(IPlaywright playwright, string browserName, int parallelism)
        {
            // Firefox and WebKit help to triage a difference (known-differences.json lists the deviations of Chromium)
            var browserType = browserName switch
            {
                "chromium" => playwright.Chromium,
                "firefox" => playwright.Firefox,
                "webkit" => playwright.Webkit,
                _ => throw new InvalidOperationException($"Unknown browser '{browserName}'"),
            };

            var session = new BrowserSession(await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }));
            session._documents[BlankUrl] = "<!DOCTYPE html>"u8.ToArray();
            foreach (var scripting in (bool[])[false, true])
            {
                var context = await session._browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = scripting });
                await context.RouteAsync("**/*", async route =>
                {
                    if (route.Request.IsNavigationRequest && session._documents.TryGetValue(route.Request.Url, out var body))
                    {
                        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Content-Type"] = "text/html; charset=utf-8" };
                        if (scripting)
                        {
                            headers["Content-Security-Policy"] = "script-src 'none'";
                        }

                        await route.FulfillAsync(new RouteFulfillOptions { Status = 200, Headers = headers, BodyBytes = body });
                    }
                    else
                    {
                        // The subresources and the other navigations (e.g. a meta refresh, an iframe) are not loaded
                        await route.AbortAsync();
                    }
                });

                var pages = new Channel();
                for (var i = 0; i < parallelism; i++)
                {
                    pages.Release(await context.NewPageAsync());
                }

                session._pages[scripting ? 1 : 0] = pages;
            }

            return session;
        }

        public Task<string[][]> ParseAsync(ComparisonMode mode, string[] inputs) => mode.Context is { } context
            ? ParseFragmentsAsync(mode.Scripting, inputs, [context])
            : ParseDocumentsAsync(mode.Scripting, inputs);

        /// <summary>Parses each input as a document (one navigation per input, on the pages of the pool).</summary>
        public async Task<string[][]> ParseDocumentsAsync(bool scripting, string[] inputs)
        {
            return await Task.WhenAll(inputs.Select(async input =>
            {
                var page = await _pages[scripting ? 1 : 0].AcquireAsync();
                try
                {
                    var url = $"https://chromium-diff.test/{Interlocked.Increment(ref _counter)}";
                    _documents[url] = Encoding.UTF8.GetBytes(input);
                    try
                    {
                        var attempt = 0;
                        while (true)
                        {
                            try
                            {
                                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                                return await page.EvaluateAsync<string[]>(_documentScript);
                            }
                            catch (PlaywrightException) when (++attempt < 3)
                            {
                                // e.g. the execution context was destroyed by a navigation (meta refresh)
                            }
                        }
                    }
                    finally
                    {
                        _documents.TryRemove(url, out _);
                    }
                }
                finally
                {
                    _pages[scripting ? 1 : 0].Release(page);
                }
            }));
        }

        /// <summary>Parses each input as a fragment in each context element (results[input * contexts + context]), in one call.</summary>
        public async Task<string[][]> ParseFragmentsAsync(bool scripting, string[] inputs, FragmentContext[] contexts)
        {
            var page = await _pages[scripting ? 1 : 0].AcquireAsync();
            try
            {
                if (page.Url != BlankUrl)
                {
                    await page.GotoAsync(BlankUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                }

                var contextArguments = contexts.Select(c => new { @namespace = c.Namespace, name = c.Name, attributes = c.Attributes.Select(a => new[] { a.Name, a.Value }).ToArray(), quirks = c.Quirks }).ToArray();
                return await page.EvaluateAsync<string[][]>(_fragmentScript, new { inputs, contexts = contextArguments });
            }
            finally
            {
                _pages[scripting ? 1 : 0].Release(page);
            }
        }

        public async ValueTask DisposeAsync() => await _browser.DisposeAsync();

        /// <summary>Returns the script of a resource, with the functions of tree.js, as a function of one argument.</summary>
        private static string CreateScript(string name) => "arg => {\n" + ReadResource("tree.js") + "\n" + ReadResource(name) + "\n}";

        private static string ReadResource(string name)
        {
            using var stream = typeof(TreeDumper).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Resource '{name}' not found");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>A pool of pages.</summary>
        private sealed class Channel
        {
            private readonly System.Threading.Channels.Channel<IPage> _channel = System.Threading.Channels.Channel.CreateUnbounded<IPage>();

            public ValueTask<IPage> AcquireAsync() => _channel.Reader.ReadAsync();

            public void Release(IPage page) => _channel.Writer.TryWrite(page);
        }
    }
}
