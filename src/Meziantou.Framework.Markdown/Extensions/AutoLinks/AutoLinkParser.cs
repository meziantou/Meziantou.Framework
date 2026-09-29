// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.AutoLinks;

/// <summary>
/// The inline parser used to for autolinks.
/// </summary>
/// <seealso cref="InlineParser" />
public class AutoLinkParser : InlineParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AutoLinkParser"/> class.
    /// </summary>
    public AutoLinkParser(AutoLinkOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));

        OpeningCharacters =
        [
            'h', // for http:// and https://
            'f', // for ftp://
            'm', // for mailto:
            't', // for tel:
            'w', // for www.
        ];

        _validPreviousCharacters = SearchValues.Create(options.ValidPreviousCharacters);
    }

    /// <summary>
    /// Gets or sets the options.
    /// </summary>
    public readonly AutoLinkOptions Options;

    private readonly SearchValues<char> _validPreviousCharacters;

    // This is a particularly expensive parser as it gets called for many common letters.
    /// <summary>
    /// Attempts to match the parser at the current position.
    /// </summary>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Previous char must be a whitespace or a punctuation
        var previousChar = slice.PeekCharExtra(-1);
        if (!previousChar.IsWhiteSpaceOrZero() && !_validPreviousCharacters.Contains(previousChar))
        {
            return false;
        }

        ReadOnlySpan<char> span = slice.AsSpan();

        Debug.Assert(span[0] is 'h' or 'f' or 'm' or 't' or 'w');

        // Precheck URL
        bool mayBeValid = span.Length >= 4 && span[0] switch
        {
            'h' => span.StartsWith("https://", StringComparison.Ordinal) || span.StartsWith("http://", StringComparison.Ordinal),
            'w' => span.StartsWith("www.", StringComparison.Ordinal), // We won't match http:/www. or /www.xxx
            'f' => span.StartsWith("ftp://", StringComparison.Ordinal),
            'm' => span.StartsWith("mailto:", StringComparison.Ordinal),
            _ => span.StartsWith("tel:", StringComparison.Ordinal),
        };

        return mayBeValid && MatchCore(processor, ref slice);
    }

    private bool MatchCore(InlineProcessor processor, ref StringSlice slice)
    {
        char c = slice.CurrentChar;
        var startPosition = slice.Start;

        // We don't bother disposing the builder as it'll realistically never grow beyond the initial stack size.
        var pendingEmphasis = new ValueStringBuilder(unsafe(stackalloc char[32]));

        // Check that an autolink is possible in the current context
        if (!IsAutoLinkValidInCurrentContext(processor, ref pendingEmphasis))
        {
            return false;
        }

        // Parse URL. The scans are remembered, as a run of text without whitespace can contain many candidates.
        var scanCache = processor.AutoLinkScanCache;
        var urlEnd = scanCache.ScanUrl(slice);
        if (urlEnd < 0)
        {
            return false;
        }

        slice.Start = urlEnd;
        var linkEnd = urlEnd;

        // If we have any pending emphasis, remove any pending emphasis characters from the end of the link
        if (pendingEmphasis.Length > 0)
        {
            // When all the characters are pending emphasis characters, the link is kept but the slice goes back to its start
            slice.Start = scanCache.GetTrailingCharactersStart(startPosition, urlEnd, pendingEmphasis.AsSpan());
            if (slice.Start > startPosition)
            {
                linkEnd = slice.Start;
            }
        }

        // The link is only copied once it is known to be valid
        var link = slice.Text.AsSpan(startPosition, linkEnd - startPosition);
        int domainOffset = 0;

        // Post-check URL
        switch (c)
        {
            case 'h':
                if (link.SequenceEqual("http://") || link.SequenceEqual("https://"))
                {
                    return false;
                }
                domainOffset = link[4] == 's' ? 8 : 7; // https:// or http://
                break;

            case 'w':
                domainOffset = 4; // www.
                break;

            case 'f':
                if (link.SequenceEqual("ftp://"))
                {
                    return false;
                }
                domainOffset = 6; // ftp://
                break;

            case 't':
                if (link.SequenceEqual("tel"))
                {
                    return false;
                }
                break;

            case 'm':
                int atIndex = scanCache.IndexOfAt(startPosition, linkEnd);
                if (atIndex == -1 ||
                    atIndex == 7) // mailto:@ - no email part
                {
                    return false;
                }
                domainOffset = atIndex + 1;
                break;
        }

        // Do not need to check if a telephone number is a valid domain
        if (c != 't' && !scanCache.IsValidDomain(startPosition, startPosition + domainOffset, linkEnd, Options.AllowDomainWithoutPeriod))
        {
            return false;
        }

        var url = link.ToString();
        var inline = new LinkInline()
        {
            Span =
            {
                Start = processor.GetSourcePosition(startPosition, out int line, out int column),
            },
            Line = line,
            Column = column,
            Url = c == 'w' ? ((Options.UseHttpsForWWWLinks ? "https://" : "http://") + url) : url,
            IsClosed = true,
            IsAutoLink = true,
        };

        int skipFromBeginning = c switch
        {
            'm' => 7, // For mailto: skip "mailto:" for content
            't' => 4, // Same but for tel:
            _ => 0
        };

        inline.Span.End = inline.Span.Start + url.Length - 1;
        inline.UrlSpan = inline.Span;
        if (processor.TrackTrivia)
        {
            inline.UnescapedUrl = new StringSlice(slice.Text, startPosition, startPosition + url.Length - 1);
        }

        inline.AppendChild(new LiteralInline()
        {
            Span = inline.Span,
            Line = line,
            Column = column,
            Content = new StringSlice(slice.Text, startPosition + skipFromBeginning, startPosition + url.Length - 1),
            IsClosed = true
        });
        processor.Inline = inline;

        if (Options.OpenInNewWindow)
        {
            inline.GetAttributes().AddPropertyIfNotExist("target", "_blank");
        }

        return true;
    }

    private static bool IsAutoLinkValidInCurrentContext(InlineProcessor processor, ref ValueStringBuilder pendingEmphasis)
    {
        // The walks below visit all the previous inlines and all the unresolved delimiters. When there are many of them, the
        // inline processor tracks what they look for.
        if (processor.Inline is { } inline && processor.TryGetAutoLinkContext(inline, engage: false, out var anchor, out var linkDelimiterBalance, out var emphasisCharacters))
        {
            return IsValidContext(anchor, linkDelimiterBalance, emphasisCharacters, ref pendingEmphasis);
        }

        // Case where there is a pending HtmlInline <a>
        var currentInline = processor.Inline;
        var visitedCount = 0;
        while (currentInline != null)
        {
            if (++visitedCount == processor.OpenContainersTrackingThreshold && processor.TryGetAutoLinkContext(processor.Inline!, engage: true, out anchor, out linkDelimiterBalance, out emphasisCharacters))
            {
                return IsValidContext(anchor, linkDelimiterBalance, emphasisCharacters, ref pendingEmphasis);
            }

            if (currentInline is HtmlInline htmlInline)
            {
                // If we have a </a> we don't expect nested <a>
                if (htmlInline.Tag.StartsWith("</a", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // If there is a pending <a>, we can't allow a link
                if (htmlInline.Tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Check previous sibling and parents in the tree
            currentInline = currentInline.PreviousSibling ?? currentInline.Parent;
        }

        // Check that we don't have any pending brackets opened (where we could have a possible markdown link)
        // NOTE: This assume that [ and ] are used for links, otherwise autolink will not work properly
        currentInline = processor.Inline;
        int countBrackets = 0;
        while (currentInline != null)
        {
            if (currentInline is LinkDelimiterInline linkDelimiterInline && linkDelimiterInline.IsActive)
            {
                if (linkDelimiterInline.Type == DelimiterType.Open)
                {
                    countBrackets++;
                }
                else if (linkDelimiterInline.Type == DelimiterType.Close)
                {
                    countBrackets--;
                }
            }
            else
            {
                // Record all pending characters for emphasis
                if (currentInline is EmphasisDelimiterInline emphasisDelimiter)
                {
                    if (!pendingEmphasis.AsSpan().Contains(emphasisDelimiter.DelimiterChar))
                    {
                        pendingEmphasis.Append(emphasisDelimiter.DelimiterChar);
                    }
                }
            }
            currentInline = currentInline.Parent;
        }

        return countBrackets <= 0;

        static bool IsValidContext(HtmlInline? anchor, int linkDelimiterBalance, string emphasisCharacters, ref ValueStringBuilder pendingEmphasis)
        {
            if (anchor is not null && !anchor.Tag.StartsWith("</a", StringComparison.OrdinalIgnoreCase) && anchor.Tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            pendingEmphasis.Append(emphasisCharacters);
            return linkDelimiterBalance <= 0;
        }
    }
}
