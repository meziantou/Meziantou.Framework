using System.Globalization;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The declarative refresh of a document: the Refresh header and <meta http-equiv="refresh">
public partial class Document
{
    private List<Action>? _completelyLoadedCallbacks;

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#will-declaratively-refresh</summary>
    internal bool WillDeclarativelyRefresh { get; private set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#shared-declarative-refresh-steps: once the refresh comes due,
    /// the browsing context navigates to the URL (when <see cref="BrowsingContextOptions.DeclarativeRefreshEnabled"/>).
    /// </summary>
    /// <param name="input">The value of the <c>Refresh</c> header or of the <c>content</c> attribute, e.g. <c>5; url=next.html</c>.</param>
    /// <param name="isMeta">Whether the refresh comes from a <c>meta</c> element: the sandboxed automatic features browsing context flag blocks it.</param>
    /// <remarks>
    /// The refresh of a meta element comes due after the later of the completely loaded time of the document and the
    /// insertion of the element: as the element is inserted now, it is the same as for the <c>Refresh</c> header.
    /// </remarks>
    internal void RunSharedDeclarativeRefreshSteps(string input, bool isMeta = false)
    {
        if (WillDeclarativelyRefresh)
            return;

        var position = 0;
        SkipAsciiWhitespace(input, ref position);
        var timeStart = position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            position++;
        }

        long time = 0;
        if (position == timeStart)
        {
            if (position >= input.Length || input[position] != '.')
                return;
        }
        else if (!long.TryParse(input.AsSpan(timeStart, position - timeStart), NumberStyles.None, CultureInfo.InvariantCulture, out time))
        {
            time = long.MaxValue;
        }

        while (position < input.Length && (char.IsAsciiDigit(input[position]) || input[position] == '.'))
        {
            position++;
        }

        var urlRecord = UrlRecord;
        if (position < input.Length)
        {
            if (input[position] is not (';' or ',') && !InfraAscii.IsAsciiWhitespace(input[position]))
                return;

            SkipAsciiWhitespace(input, ref position);
            if (position < input.Length && input[position] is ';' or ',')
            {
                position++;
            }

            SkipAsciiWhitespace(input, ref position);
        }

        if (position < input.Length)
        {
            var urlString = input[position..];
            if (TryConsumeUrlPrefix(input, ref position))
            {
                // Skip quotes
                var quote = position < input.Length && input[position] is '\'' or '"' ? input[position++] : '\0';
                urlString = input[position..];
                if (quote != '\0' && urlString.IndexOf(quote, StringComparison.Ordinal) is var index and >= 0)
                {
                    urlString = urlString[..index];
                }
            }

            // Parse
            if (UrlParser.Parse(urlString, DocumentBaseUrl, Encoding) is not { } parsedUrl || parsedUrl.Scheme == "javascript")
                return;

            urlRecord = parsedUrl;
        }

        WillDeclarativelyRefresh = true;
        if (BrowsingContext is not { Options.DeclarativeRefreshEnabled: true } browsingContext)
            return;

        // The refresh comes due "time" seconds after the later of the completely loaded time and the insertion of the meta element
        var delay = TimeSpan.FromSeconds(Math.Min(time, int.MaxValue));
        var eventLoop = browsingContext.EventLoop;
        void StartTimer() => eventLoop.QueueTimer(delay, () =>
        {
            if (BrowsingContext is { } navigable && ReferenceEquals(navigable.Document, this) && (!isMeta || (ActiveSandboxingFlags & SandboxingFlags.AutomaticFeatures) == SandboxingFlags.None))
            {
                // All the browsers reload the document when the URL of the refresh is the URL of the document (the navigation
                // type of its navigate event is "reload"); the specification navigates with "replace"
                if (Dom.BrowsingContext.UrlEquals(urlRecord, UrlRecord, excludeFragments: false))
                {
                    navigable.ReloadNavigable();
                }
                else
                {
                    navigable.Navigate(urlRecord, this, NavigationHistoryBehavior.Replace);
                }
            }
        });

        if (CompletelyLoaded)
        {
            StartTimer();
        }
        else
        {
            (_completelyLoadedCallbacks ??= []).Add(StartTimer);
        }

        static bool TryConsumeUrlPrefix(string input, ref int position)
        {
            // "url=" (ASCII case-insensitive, with optional whitespace before '='): otherwise, the rest is the URL, unless it starts with a quote
            if (position >= input.Length || input[position] is not ('U' or 'u'))
                return true;

            var start = position;
            position++;
            if (position < input.Length && input[position] is 'R' or 'r')
            {
                position++;
                if (position < input.Length && input[position] is 'L' or 'l')
                {
                    position++;
                    SkipAsciiWhitespace(input, ref position);
                    if (position < input.Length && input[position] == '=')
                    {
                        position++;
                        SkipAsciiWhitespace(input, ref position);
                        return true;
                    }
                }
            }

            position = start;
            return false;
        }
    }

    /// <summary>Runs the callbacks waiting for the document to be completely loaded (https://html.spec.whatwg.org/multipage/document-lifecycle.html#completely-loaded-time).</summary>
    private void RunCompletelyLoadedCallbacks()
    {
        if (_completelyLoadedCallbacks is not { } callbacks)
            return;

        _completelyLoadedCallbacks = null;
        foreach (var callback in callbacks)
        {
            callback();
        }
    }

    private static void SkipAsciiWhitespace(string input, ref int position)
    {
        while (position < input.Length && InfraAscii.IsAsciiWhitespace(input[position]))
        {
            position++;
        }
    }
}
