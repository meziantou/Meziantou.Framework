using System.Globalization;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A Markdown document: its blocks, and the end of the text.</summary>
public sealed class MarkdownDocumentSyntax : MarkdownSyntaxNode
{
    internal MarkdownDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;
    private Dictionary<string, MarkdownLinkReferenceDefinitionSyntax>? _definitions;
    private Dictionary<string, MarkdownAbbreviationDefinitionSyntax>? _abbreviations;
    private Dictionary<string, MarkdownFootnoteDefinitionSyntax>? _footnotes;
    private Dictionary<MarkdownHeadingSyntax, string>? _headingIdentifiers;
    private Dictionary<string, MarkdownHeadingSyntax>? _headings;

    /// <summary>Gets the blocks of the document, in the order of the text.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    /// <summary>Gets the token that ends the document, which holds the trivia after the last block.</summary>
    public SyntaxToken EndOfFileToken => GetToken(SyntaxKind.EndOfFileToken);

    /// <summary>Gets the link reference definitions of the document, in the order of the text.</summary>
    public IEnumerable<MarkdownLinkReferenceDefinitionSyntax> LinkReferenceDefinitions => DescendantNodes(IsBlockContainer).OfType<MarkdownLinkReferenceDefinitionSyntax>();

    // The nodes whose children are blocks, where definitions can be
    internal static bool IsBlockContainer(SyntaxNode node) => node is MarkdownDocumentSyntax or MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownListSyntax or MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownCustomContainerSyntax or MarkdownDefinitionListSyntax or MarkdownDefinitionItemSyntax or MarkdownFigureSyntax or MarkdownFooterSyntax;

    /// <summary>Finds the definition of a footnote: the first one with the same label, compared as CommonMark compares link labels.</summary>
    /// <param name="label">The label, with its <c>^</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownFootnoteDefinitionSyntax? GetFootnoteDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_footnotes is null)
        {
            var footnotes = new Dictionary<string, MarkdownFootnoteDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in Blocks.OfType<MarkdownFootnoteDefinitionSyntax>())
            {
                footnotes.TryAdd(Scanners.NormalizeLabel(definition.Label), definition);
            }

            _footnotes = footnotes;
        }

        return _footnotes.GetValueOrDefault(Scanners.NormalizeLabel(label));
    }

    /// <summary>Finds the definition of an abbreviation: the last one with the same label, compared as the text writes it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownAbbreviationDefinitionSyntax? GetAbbreviationDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_abbreviations is null)
        {
            var abbreviations = new Dictionary<string, MarkdownAbbreviationDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in DescendantNodes(IsBlockContainer).OfType<MarkdownAbbreviationDefinitionSyntax>())
            {
                abbreviations[definition.Label] = definition;
            }

            _abbreviations = abbreviations;
        }

        return _abbreviations.GetValueOrDefault(label);
    }

    /// <summary>Finds the definition of a link label: the first one with the same label, compared as CommonMark does.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownLinkReferenceDefinitionSyntax? GetLinkReferenceDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_definitions is null)
        {
            var definitions = new Dictionary<string, MarkdownLinkReferenceDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in LinkReferenceDefinitions)
            {
                definitions.TryAdd(Scanners.NormalizeLabel(definition.Label), definition);
            }

            _definitions = definitions;
        }

        return _definitions.GetValueOrDefault(Scanners.NormalizeLabel(label));
    }

    internal MarkdownParseOptions Options => (SyntaxTree as MarkdownSyntaxTree)?.Options ?? MarkdownParseOptions.Default;

    /// <summary>Gets the identifier of a heading of the document: the id of its generic attributes, or the one of auto-identifiers.</summary>
    internal string? GetHeadingIdentifier(MarkdownHeadingSyntax heading)
    {
        if (_headingIdentifiers is null)
        {
            var options = Options;
            var identifiers = new Dictionary<MarkdownHeadingSyntax, string>();
            if (options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes))
            {
                // The id of the last attributes wins
                MarkdownGenericAttributesSyntax.FindTargets(this, (attributes, target) =>
                {
                    if (target is MarkdownHeadingSyntax heading && attributes.Id is { } id)
                    {
                        identifiers[heading] = id;
                    }
                });
            }

            if (options.Extensions.HasFlag(MarkdownExtensions.AutoIdentifiers))
            {
                // As in Markdig, the identifier of a heading is unique among the generated ones, which are made in the order of the
                // text, except that the footnotes are read where the first one is
                var used = new HashSet<string>(StringComparer.Ordinal);
                var lastSuffixes = new Dictionary<string, int>(StringComparer.Ordinal);
                var footnotesRead = false;
                foreach (var block in Blocks)
                {
                    if (block is MarkdownFootnoteDefinitionSyntax)
                    {
                        if (!footnotesRead)
                        {
                            foreach (var footnote in Blocks.OfType<MarkdownFootnoteDefinitionSyntax>())
                            {
                                AddIdentifiers(footnote);
                            }

                            footnotesRead = true;
                        }

                        continue;
                    }

                    AddIdentifiers(block);
                }

                void AddIdentifiers(MarkdownBlockSyntax block)
                {
                    foreach (var heading in block.DescendantNodesAndSelf(IsBlockContainer).OfType<MarkdownHeadingSyntax>())
                    {
                        if (identifiers.ContainsKey(heading))
                        {
                            continue;
                        }

                        var baseIdentifier = HeadingIdentifier.Create(heading.GetIdentifierText(), options.AutoIdentifierOptions);
                        if (baseIdentifier.Length == 0)
                        {
                            baseIdentifier = "section";
                        }

                        // The suffixes below the last one used for this base are all taken
                        var identifier = baseIdentifier;
                        if (!used.Add(identifier))
                        {
                            var suffix = lastSuffixes.GetValueOrDefault(baseIdentifier);
                            do
                            {
                                suffix++;
                                identifier = baseIdentifier + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                            }
                            while (!used.Add(identifier));

                            lastSuffixes[baseIdentifier] = suffix;
                        }

                        identifiers[heading] = identifier;
                    }
                }
            }

            _headingIdentifiers = identifiers;
        }

        return _headingIdentifiers.GetValueOrDefault(heading);
    }

    /// <summary>Finds the heading that a reference link without definition links to with auto-identifiers: the last one whose first line has the label.</summary>
    internal MarkdownHeadingSyntax? GetHeading(string label)
    {
        var options = Options;
        if (!options.Extensions.HasFlag(MarkdownExtensions.AutoIdentifiers) || !options.AutoIdentifierOptions.HasFlag(MarkdownAutoIdentifierOptions.AutoLink))
        {
            return null;
        }

        if (_headings is null)
        {
            var headings = new Dictionary<string, MarkdownHeadingSyntax>(StringComparer.Ordinal);
            foreach (var heading in DescendantNodes(IsBlockContainer).OfType<MarkdownHeadingSyntax>())
            {
                // A label has a character that is not whitespace
                if (Scanners.NormalizeLabel(heading.GetFirstLineLabel(options)) is { Length: > 0 } headingLabel)
                {
                    headings[headingLabel] = heading;
                }
            }

            _headings = headings;
        }

        return _headings.GetValueOrDefault(Scanners.NormalizeLabel(label));
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownDocument(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownDocument(this);
    }
}
