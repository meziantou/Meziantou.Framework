using System.Globalization;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

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
    private Dictionary<MarkdownSyntaxNode, TargetAttributes>? _genericAttributes;

    // Whether the document has generic attributes: 0 when not computed yet, 1 when it has none, 2 when it has some
    private int _hasGenericAttributes;
    private Dictionary<string, MarkdownHeadingSyntax>? _headings;

    /// <summary>Gets the blocks of the document, in the order of the text.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    /// <summary>Gets the token that ends the document, which holds the trivia after the last block.</summary>
    public SyntaxToken EndOfFileToken => GetToken(SyntaxKind.EndOfFileToken);

    /// <summary>Gets the link reference definitions of the document, in the order of the text.</summary>
    public IEnumerable<MarkdownLinkReferenceDefinitionSyntax> LinkReferenceDefinitions => GetBlocks<MarkdownLinkReferenceDefinitionSyntax>();

    // The nodes whose children are blocks, where definitions can be
    internal static bool IsBlockContainer(SyntaxNode node) => node is MarkdownDocumentSyntax or MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownListSyntax or MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownCustomContainerSyntax or MarkdownDefinitionListSyntax or MarkdownDefinitionItemSyntax or MarkdownFigureSyntax or MarkdownFooterSyntax or MarkdownExtensionBlockSyntax;

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
            foreach (var definition in GetBlocks<MarkdownAbbreviationDefinitionSyntax>())
            {
                abbreviations[definition.Label] = definition;
            }

            _abbreviations = abbreviations;
        }

        return _abbreviations.GetValueOrDefault(label);
    }

    /// <summary>Finds the definition of a link label: the first one with the same label, compared as CommonMark does. In a cell of a grid table, the definitions of the tree of the table are found too.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownLinkReferenceDefinitionSyntax? GetLinkReferenceDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_definitions is null)
        {
            var definitions = new Dictionary<string, MarkdownLinkReferenceDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in GetBlocks<MarkdownLinkReferenceDefinitionSyntax>())
            {
                definitions.TryAdd(Scanners.NormalizeLabel(definition.Label), definition);
            }

            _definitions = definitions;
        }

        // The content of a cell of a grid table can use the definitions of the document of the table
        return _definitions.GetValueOrDefault(Scanners.NormalizeLabel(label)) ?? (SyntaxTree as MarkdownSyntaxTree)?.Container?.GetRoot().GetLinkReferenceDefinition(label);
    }

    /// <summary>
    /// Finds the line of the block that is the closest to a line, as Markdig does for its pragma lines: an editor scrolls its
    /// preview to the element whose id is <c>pragma-line-N</c>, where N is the line that this method returns.
    /// </summary>
    /// <param name="line">The zero-based number of a line of the text.</param>
    /// <returns>The zero-based number of the line where the block starts, or 0 when the document has no block.</returns>
    public int FindClosestLine(int line) => FindClosestBlock(this, line) is { } block ? MarkdigLines.GetLine(block) : 0;

    private static SyntaxNode? FindClosestBlock(SyntaxNode container, int line)
    {
        var blocks = IsBlockContainer(container) ? container.ChildNodes().OfType<MarkdownBlockSyntax>().ToList() : [];
        if (blocks.Count == 0)
        {
            return container is MarkdownBlockSyntax && MarkdigLines.GetLine(container) == line ? container : null;
        }

        var lowerIndex = 0;
        var upperIndex = blocks.Count - 1;
        while (lowerIndex <= upperIndex)
        {
            var middleIndex = ((upperIndex - lowerIndex) / 2) + lowerIndex;
            var comparison = MarkdigLines.GetLine(blocks[middleIndex]).CompareTo(line);
            if (comparison == 0)
            {
                return blocks[middleIndex];
            }

            if (comparison < 0)
            {
                lowerIndex = middleIndex + 1;
            }
            else
            {
                upperIndex = middleIndex - 1;
            }
        }

        // Between two blocks: the previous one, unless the line is past the middle of the lines between them
        if (lowerIndex > 0 && lowerIndex < blocks.Count)
        {
            var previous = FindClosestBlock(blocks[lowerIndex - 1], line) ?? blocks[lowerIndex - 1];
            var next = FindClosestBlock(blocks[lowerIndex], line) ?? blocks[lowerIndex];
            var previousLine = MarkdigLines.GetLine(previous);
            var nextLine = MarkdigLines.GetLine(next);
            if (previousLine == line)
            {
                return previous;
            }

            if (nextLine == line)
            {
                return next;
            }

            return (line - previousLine) * 1.0 / (nextLine - previousLine) < 0.5 ? previous : next;
        }

        if (lowerIndex == 0)
        {
            return FindClosestBlock(blocks[0], line) ?? blocks[0];
        }

        return FindClosestBlock(blocks[^1], line) ?? blocks[^1];
    }

    internal MarkdownParseOptions Options => (SyntaxTree as MarkdownSyntaxTree)?.Options ?? MarkdownParseOptions.Default;

    /// <summary>Gets a value indicating whether the document has generic attributes, which most documents have none of.</summary>
    /// <remarks>The green tree is searched: it skips the tokens and creates no red node.</remarks>
    internal bool HasGenericAttributes
    {
        get
        {
            if (_hasGenericAttributes == 0)
            {
                _hasGenericAttributes = Options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes) && ContainsKind(Green, SyntaxKind.GenericAttributes) ? 2 : 1;
            }

            return _hasGenericAttributes == 2;
        }
    }

    /// <summary>Gets the generic attributes of a node of the document, merged from every <c>{...}</c> that is for it, and these <c>{...}</c>.</summary>
    /// <remarks>The targets of all the attributes are found once, in one pass over the document.</remarks>
    internal (MarkdownGenericAttributes Attributes, IReadOnlyList<MarkdownGenericAttributesSyntax> Nodes) GetGenericAttributes(MarkdownSyntaxNode target)
    {
        if (!HasGenericAttributes)
            return (MarkdownGenericAttributes.Empty, []);

        if (_genericAttributes is null)
        {
            var targets = new Dictionary<MarkdownSyntaxNode, TargetAttributes>();
            _ = MarkdownGenericAttributesSyntax.FindTargets(this, (attributes, target) =>
            {
                if (!targets.TryGetValue(target, out var targetAttributes))
                {
                    targetAttributes = new TargetAttributes();
                    targets.Add(target, targetAttributes);
                }

                targetAttributes.Nodes.Add(attributes);
            });

            _genericAttributes = targets;
        }

        return _genericAttributes.TryGetValue(target, out var result) ? (result.GetAttributes(), result.Nodes) : (MarkdownGenericAttributes.Empty, []);
    }

    /// <summary>Gets the identifier of a heading of the document: the id of its generic attributes, or the one of auto-identifiers.</summary>
    internal string? GetHeadingIdentifier(MarkdownHeadingSyntax heading)
    {
        if (_headingIdentifiers is null)
        {
            var options = Options;
            var identifiers = new Dictionary<MarkdownHeadingSyntax, string>();
            if (HasGenericAttributes)
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
            foreach (var heading in GetBlocks<MarkdownHeadingSyntax>())
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

    // The blocks of a kind, in the order of the text, as DescendantNodes(IsBlockContainer).OfType<T>() finds them, without its
    // iterators and without enumerating the tokens of the containers
    private List<T> GetBlocks<T>()
        where T : MarkdownSyntaxNode
    {
        var blocks = new List<T>();
        AddBlocks(this, blocks);
        return blocks;

        static void AddBlocks(SyntaxNode container, List<T> blocks)
        {
            foreach (var child in container.ChildNodesAndTokens())
            {
                if (child.AsNode() is not { } node)
                {
                    continue;
                }

                if (node is T block)
                {
                    blocks.Add(block);
                }

                if (IsBlockContainer(node))
                {
                    AddBlocks(node, blocks);
                }
            }
        }
    }

    private static bool ContainsKind(GreenNode root, SyntaxKind kind)
    {
        var stack = new Stack<GreenNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            if (node.RawKind == (int)kind)
            {
                return true;
            }

            for (var i = 0; i < node.SlotCount; i++)
            {
                if (node.GetSlot(i) is { IsToken: false } child)
                {
                    stack.Push(child);
                }
            }
        }

        return false;
    }

    // The attributes that are for a node, in the order Markdig applies them
    private sealed class TargetAttributes
    {
        private MarkdownGenericAttributes? _attributes;

        public List<MarkdownGenericAttributesSyntax> Nodes { get; } = [];

        // As in the HTML renderer, the id of the last attributes wins, the classes are added, and a property is kept from the first
        // attributes that have it
        public MarkdownGenericAttributes GetAttributes()
        {
            if (_attributes is not null)
                return _attributes;

            string? id = null;
            List<string>? blockLineClasses = null;
            var classes = new List<string>();
            var properties = new List<KeyValuePair<string, string?>>();
            foreach (var attributes in Nodes)
            {
                id = attributes.Id ?? id;
                if (attributes.IsBlockLine)
                {
                    blockLineClasses = [.. attributes.Classes];
                }
                else
                {
                    classes.AddRange(attributes.Classes);
                }

                var existingCount = properties.Count;
                foreach (var property in attributes.Properties)
                {
                    if (properties.FindIndex(0, existingCount, existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)) < 0)
                    {
                        properties.Add(property);
                    }
                }
            }

            // The classes of the first line of a block come first, as the renderer writes them
            if (blockLineClasses is not null)
            {
                classes.InsertRange(0, blockLineClasses);
            }

            _attributes = id is null && classes.Count == 0 && properties.Count == 0 ? MarkdownGenericAttributes.Empty : new MarkdownGenericAttributes(id, classes, properties);
            return _attributes;
        }
    }
}
