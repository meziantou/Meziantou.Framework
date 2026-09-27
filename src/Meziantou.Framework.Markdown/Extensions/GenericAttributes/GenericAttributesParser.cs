// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// An inline parser used to parse a HTML attributes that can be attached to the previous <see cref="Inline"/> or current <see cref="Block"/>.
/// </summary>
/// <seealso cref="InlineParser" />
public class GenericAttributesParser : InlineParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenericAttributesParser"/> class.
    /// </summary>
    public GenericAttributesParser()
    {
        OpeningCharacters = ['{'];
    }

    /// <summary>
    /// Gets or sets the predicate that decides whether an attribute parsed from the Markdown (other than the id and the
    /// classes) is kept. The default value is <see cref="GenericAttributesExtension.IsSafeAttributeName"/>.
    /// </summary>
    public Func<string, bool> AttributeFilter { get; set; } = GenericAttributesExtension.IsSafeAttributeName;

    /// <summary>
    /// Attempts to match the parser at the current position.
    /// </summary>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var startPosition = slice.Start;

        // Remembers the failed scans of this inline text, so that a '{' that cannot start attributes fails without scanning the text again
        var scanCache = processor.GenericAttributesScanCache;
        scanCache.SetText(slice);
        if (scanCache.IsKnownInvalid(startPosition))
        {
            return false;
        }

        if (TryParse(ref slice, out HtmlAttributes? attributes, out var scanEnd))
        {
            RemoveFilteredProperties(attributes, AttributeFilter);

            var inline = processor.Inline;

            // If the current object to attach is either a literal or delimiter
            // try to find a suitable parent, otherwise attach the html attributes to the block
            if (inline is LiteralInline)
            {
                inline = processor.FindNonDelimiterParent(inline);
            }
            var objectToAttach = inline is null || inline == processor.Root ? (MarkdownObject)processor.Block! : inline;

            // If the current block is a Paragraph, but only the HtmlAttributes is used,
            // Try to attach the attributes to the following block
            if (objectToAttach is ParagraphBlock paragraph &&
                paragraph.Inline!.FirstChild is null &&
                processor.Inline is null &&
                slice.IsEmptyOrWhitespace())
            {
                var parent = paragraph.Parent!;
                var indexOfParagraph = parent.IndexOf(paragraph);
                if (indexOfParagraph + 1 < parent.Count)
                {
                    objectToAttach = parent[indexOfParagraph + 1];
                    // We can remove the paragraph as it is empty
                    paragraph.RemoveAfterProcessInlines = true;
                }
            }

            var currentHtmlAttributes = objectToAttach.GetAttributes();
            attributes.CopyTo(currentHtmlAttributes, true, false);

            // Update the position of the attributes
            currentHtmlAttributes.Span.Start = processor.GetSourcePosition(startPosition, out int line, out int column);
            currentHtmlAttributes.Line = line;
            currentHtmlAttributes.Column = column;
            currentHtmlAttributes.Span.End = currentHtmlAttributes.Span.Start + slice.Start - startPosition - 1;

            // Attributes that end the text are not part of it, so the whitespace before them ends the text and is trimmed, like
            // the whitespace at the end of a paragraph. With trivia, it is kept so that the roundtrip does not lose it.
            if (!processor.TrackTrivia && slice.IsEmptyOrWhitespace())
            {
                var lastInline = processor.Inline;
                while (lastInline is LiteralInline literal)
                {
                    var length = literal.Content.Length;
                    if (!literal.Content.TrimEnd())
                    {
                        literal.Span.End -= length - literal.Content.Length;
                        break;
                    }

                    // Other attributes can separate the literal from the whitespace before it
                    lastInline = literal.PreviousSibling;
                    literal.Remove();
                    processor.Inline = null;
                }
            }

            // We don't set the processor.Inline as we don't want to add attach attributes to a particular entity
            return true;
        }

        scanCache.AddFailedScan(startPosition, scanEnd);
        return false;
    }

    /// <summary>
    /// Tries to extra from the current position of a slice an HTML attributes {...}
    /// </summary>
    /// <param name="slice">The slice to parse.</param>
    /// <param name="attributes">The output attributes or null if not found or invalid</param>
    /// <returns><c>true</c> if parsing the HTML attributes was successful</returns>
    public static bool TryParse(ref StringSlice slice, [NotNullWhen(true)] out HtmlAttributes? attributes)
    {
        return TryParse(ref slice, out attributes, out _);
    }

    /// <summary>
    /// Tries to extract HTML attributes {...} from the current position of a slice, and reports where the scan stopped.
    /// </summary>
    /// <param name="slice">The slice to parse.</param>
    /// <param name="attributes">The output attributes or null if not found or invalid</param>
    /// <param name="scanEnd">The position where the scan stopped.</param>
    /// <returns><c>true</c> if parsing the HTML attributes was successful</returns>
    /// <remarks><see cref="ComputeScanOutcomes"/> follows the same steps: keep them in sync.</remarks>
    internal static bool TryParse(ref StringSlice slice, [NotNullWhen(true)] out HtmlAttributes? attributes, out int scanEnd)
    {
        attributes = null;
        if (slice.PeekCharExtra(-1) == '{')
        {
            scanEnd = slice.Start;
            return false;
        }

        var line = slice;

        string? id = null;
        List<string>? classes = null;
        List<KeyValuePair<string, string?>>? properties = null;

        bool isValid = false;
        var c = line.NextChar();
        while (true)
        {
            if (c == '}')
            {
                isValid = true;
                line.SkipChar(); // skip }
                // skip line breaks
                if (line.CurrentChar == '\n')
                {
                    line.SkipChar();
                }
                else if (line.CurrentChar == '\r' && line.PeekChar() == '\n')
                {
                    line.Start += 2;
                }
                break;
            }

            if (c == '\0')
            {
                break;
            }

            bool isClass = c == '.';
            if (c == '#' || isClass)
            {
                c = line.NextChar(); // Skip #
                var start = line.Start;
                // Get all non-whitespace characters following a #
                // But stop if we found a } or \0
                while (c != '}' && !c.IsWhiteSpaceOrZero())
                {
                    c = line.NextChar();
                }
                // An empty id or class makes the attributes invalid
                var end = line.Start - 1;
                if (end < start)
                {
                    break;
                }
                var text = slice.Text.Substring(start, end - start + 1);
                if (isClass)
                {
                    classes ??= new List<string>();
                    classes.Add(text);
                }
                else
                {
                    id = text;
                }
                continue;
            }

            if (!c.IsWhitespace())
            {
                // Parse the attribute name
                if (!IsStartAttributeName(c))
                {
                    break;
                }
                var startName = line.Start;
                while (true)
                {
                    c = line.NextChar();
                    if (!IsAttributeNameChar(c))
                    {
                        break;
                    }
                }
                var name = slice.Text.Substring(startName, line.Start - startName);

                var hasSpace = c.IsSpaceOrTab();

                // Skip any whitespaces
                line.TrimStart();
                c = line.CurrentChar;

                // Handle boolean properties that are not followed by =
                if ((hasSpace && (c == '.' || c == '#' || IsStartAttributeName(c))) || c == '}')
                {
                    properties ??= new ();

                    // Add a null value for the property
                    properties.Add(new KeyValuePair<string, string?>(name, null));
                    continue;
                }

                // Else we expect a regular property
                if (line.CurrentChar != '=')
                {
                    break;
                }

                // Go to next char, skip any spaces
                line.SkipChar();
                line.TrimStart();

                int startValue;
                int endValue;

                c = line.CurrentChar;
                // Parse a quoted string
                if (c == '\'' || c == '"')
                {
                    char openingStringChar = c;
                    startValue = line.Start + 1;
                    while (true)
                    {
                        c = line.NextChar();
                        if (c == '\0')
                        {
                            scanEnd = line.Start;
                            return false;
                        }
                        if (c == openingStringChar)
                        {
                            break;
                        }
                    }
                    endValue = line.Start - 1;
                    c = line.NextChar(); // Skip closing opening string char
                }
                else
                {
                    // Parse until we match a space or a special html character
                    startValue = line.Start;
                    bool valid = false;
                    while (true)
                    {
                        if (c == '\0')
                        {
                            scanEnd = line.Start;
                            return false;
                        }
                        if (c.IsWhitespace() || c == '}')
                        {
                            break;
                        }
                        c = line.NextChar();
                        valid = true;
                    }
                    endValue = line.Start - 1;
                    if (!valid)
                    {
                        break;
                    }
                }

                var value = slice.Text.Substring(startValue, endValue - startValue + 1);

                properties ??= new();
                properties.Add(new KeyValuePair<string, string?>(name, value));
                continue;
            }

            c = line.NextChar();
        }

        if (isValid)
        {
            attributes = new HtmlAttributes()
            {
                Id = id,
                Classes = classes,
                Properties = properties
            };

            // Assign back the current processor of the line to
            slice = line;
        }

        scanEnd = line.Start;
        return isValid;
    }

    private static bool IsStartAttributeName(char c)
    {
        return c.IsAlpha() || c == '_' || c == ':';
    }

    private static bool IsAttributeNameChar(char c)
    {
        return c.IsAlphaNumeric() || c == '_' || c == ':' || c == '.' || c == '-';
    }

    // The steps of TryParse that can be reached at a position of the text. Their outcome only depends on this position.
    private const byte AttributesValid = 1;     // In the main loop, the current character leads to the closing '}'
    private const byte ValueValid = 2;          // A value (quoted or not) starting here leads to the closing '}'
    private const byte AfterEqualsValid = 4;    // The value that follows the '=' just before this position leads to the closing '}'
    private const byte AfterNameValid = 8;      // The attribute name that ends just before this position leads to the closing '}'

    /// <summary>
    /// Determines whether a value computed by <see cref="ComputeScanOutcomes"/> for the position following a '{' means that
    /// <see cref="TryParse(ref StringSlice, out HtmlAttributes?, out int)"/> can succeed at this '{'.
    /// </summary>
    internal static bool IsValidScanOutcome(byte outcome) => (outcome & AttributesValid) != 0;

    /// <summary>
    /// Computes, in linear time, whether <see cref="TryParse(ref StringSlice, out HtmlAttributes?, out int)"/> succeeds
    /// when it starts at each '{' of <paramref name="text"/> from <paramref name="start"/> - 1 to <paramref name="end"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The position of the first character following a '{'.</param>
    /// <param name="end">The position of the last character of the slice.</param>
    /// <param name="outcomes">Receives an outcome for each position from <paramref name="start"/> to <paramref name="end"/> + 1, to pass to <see cref="IsValidScanOutcome(byte)"/>.</param>
    /// <remarks>
    /// This follows the steps of <see cref="TryParse(ref StringSlice, out HtmlAttributes?, out int)"/>, except the
    /// check of the character preceding the '{'. Each step only goes forward and only depends on its position, so the
    /// outcome of the steps is computed from the end of the text, where no attributes can succeed.
    /// </remarks>
    internal static void ComputeScanOutcomes(string text, int start, int end, Span<byte> outcomes)
    {
        Debug.Assert(outcomes.Length == end + 2 - start);

        // The first position at or after the current one where each scan of TryParse stops. Beyond the end, the scans read '\0'.
        var nextIdentifierEnd = end + 1;    // '}', a whitespace or '\0' ends an id, a class or an unquoted value
        var nextNonWhitespace = end + 1;
        var nextSingleQuote = end + 1;      // '\0' ends a quoted value too
        var nextDoubleQuote = end + 1;
        var nextNameEnd = end + 1;

        for (var position = end + 1; position >= start; position--)
        {
            var c = position <= end ? text[position] : '\0';

            // The scans that start after the current position
            var identifierEndAfter = nextIdentifierEnd;
            var singleQuoteAfter = nextSingleQuote;
            var doubleQuoteAfter = nextDoubleQuote;
            var nameEndAfter = nextNameEnd;

            if (c == '}' || c.IsWhiteSpaceOrZero())
            {
                nextIdentifierEnd = position;
            }

            if (!c.IsWhitespace())
            {
                nextNonWhitespace = position;
            }

            if (c is '\'' or '\0')
            {
                nextSingleQuote = position;
            }

            if (c is '"' or '\0')
            {
                nextDoubleQuote = position;
            }

            if (!IsAttributeNameChar(c))
            {
                nextNameEnd = position;
            }

            // The main loop, reading c
            bool attributesValid;
            if (c == '}')
            {
                attributesValid = true;
            }
            else if (c == '\0')
            {
                attributesValid = false;
            }
            else if (c is '#' or '.')
            {
                // An id or a class runs to the next '}' or whitespace, and cannot be empty
                attributesValid = identifierEndAfter != position + 1 && Get(outcomes, start, identifierEndAfter, AttributesValid);
            }
            else if (!c.IsWhitespace())
            {
                attributesValid = IsStartAttributeName(c) && Get(outcomes, start, nameEndAfter, AfterNameValid);
            }
            else
            {
                attributesValid = Get(outcomes, start, position + 1, AttributesValid);
            }

            // A value starting with c
            bool valueValid;
            if (c is '\'' or '"')
            {
                var closingQuote = c == '\'' ? singleQuoteAfter : doubleQuoteAfter;
                valueValid = closingQuote <= end && text[closingQuote] == c && Get(outcomes, start, closingQuote + 1, AttributesValid);
            }
            else
            {
                // An unquoted value cannot be empty, and fails when it reaches '\0'
                valueValid = nextIdentifierEnd != position && nextIdentifierEnd <= end && text[nextIdentifierEnd] != '\0' && Get(outcomes, start, nextIdentifierEnd, AttributesValid);
            }

            // After a '=', the whitespaces are skipped
            var afterEqualsValid = nextNonWhitespace == position ? valueValid : Get(outcomes, start, nextNonWhitespace, ValueValid);

            // After an attribute name, the whitespaces are skipped
            bool afterNameValid;
            var next = nextNonWhitespace <= end ? text[nextNonWhitespace] : '\0';
            if ((c.IsSpaceOrTab() && (next == '.' || next == '#' || IsStartAttributeName(next))) || next == '}')
            {
                // A boolean attribute
                afterNameValid = nextNonWhitespace == position ? attributesValid : Get(outcomes, start, nextNonWhitespace, AttributesValid);
            }
            else
            {
                afterNameValid = next == '=' && Get(outcomes, start, nextNonWhitespace + 1, AfterEqualsValid);
            }

            outcomes[position - start] = (byte)((attributesValid ? AttributesValid : 0) | (valueValid ? ValueValid : 0) | (afterEqualsValid ? AfterEqualsValid : 0) | (afterNameValid ? AfterNameValid : 0));
        }

        static bool Get(Span<byte> outcomes, int start, int position, byte step)
        {
            return (outcomes[position - start] & step) != 0;
        }
    }

    internal static void RemoveFilteredProperties(HtmlAttributes attributes, Func<string, bool> filter)
    {
        attributes.Properties?.RemoveAll(property => !filter(property.Key));
    }
}
