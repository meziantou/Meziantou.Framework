namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The syntax extensions of options, indexed for the parser: the inline extensions of each trigger character, and the delimiter
/// extension of each character. The index of an extension is its position in the options, which the tree records.
/// </summary>
internal sealed class SyntaxExtensionSet
{
    private readonly MarkdownInlineExtension[]?[] _asciiInlineExtensions = new MarkdownInlineExtension[]?[128];
    private readonly Dictionary<char, MarkdownInlineExtension[]>? _otherInlineExtensions;
    private readonly int[] _delimiterIndexes = new int[128];
    private readonly Dictionary<MarkdownSyntaxExtension, int> _indexes = new(ReferenceEqualityComparer.Instance);
    private readonly MarkdownFencedBlockExtension[]?[] _asciiFencedBlockExtensions = new MarkdownFencedBlockExtension[]?[128];
    private readonly Dictionary<char, MarkdownFencedBlockExtension[]>? _otherFencedBlockExtensions;

    public SyntaxExtensionSet(MarkdownSyntaxExtension[] extensions, string paramName)
    {
        Extensions = extensions;
        Array.Fill(_delimiterIndexes, -1);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var inlineExtensions = new Dictionary<char, List<MarkdownInlineExtension>>();
        var fencedBlockExtensions = new Dictionary<char, List<MarkdownFencedBlockExtension>>();
        var paragraphExtensions = new List<MarkdownParagraphExtension>();
        var triggers = new System.Text.StringBuilder();
        for (var i = 0; i < extensions.Length; i++)
        {
            var extension = extensions[i] ?? throw new ArgumentException("The syntax extensions cannot contain null.", paramName);
            if (!names.Add(extension.Name))
            {
                throw new ArgumentException($"Two syntax extensions are named '{extension.Name}'.", paramName);
            }

            if (!_indexes.TryAdd(extension, i))
            {
                throw new ArgumentException($"The syntax extension '{extension.Name}' is added twice.", paramName);
            }

            switch (extension)
            {
                case MarkdownInlineExtension inline:
                    foreach (var c in inline.TriggerCharacters)
                    {
                        if (!inlineExtensions.TryGetValue(c, out var list))
                        {
                            inlineExtensions[c] = list = [];
                            triggers.Append(c);
                        }

                        if (!list.Contains(inline))
                        {
                            list.Add(inline);
                        }
                    }

                    break;

                case MarkdownDelimiterExtension delimiter:
                    if (_delimiterIndexes[delimiter.Character] >= 0)
                    {
                        throw new ArgumentException($"The delimiter extensions '{extensions[_delimiterIndexes[delimiter.Character]].Name}' and '{delimiter.Name}' use the character '{delimiter.Character}'.", paramName);
                    }

                    _delimiterIndexes[delimiter.Character] = i;
                    triggers.Append(delimiter.Character);
                    break;

                case MarkdownFencedBlockExtension fenced:
                    foreach (var c in fenced.TriggerCharacters)
                    {
                        if (!fencedBlockExtensions.TryGetValue(c, out var list))
                        {
                            fencedBlockExtensions[c] = list = [];
                        }

                        if (!list.Contains(fenced))
                        {
                            list.Add(fenced);
                        }
                    }

                    break;

                case MarkdownParagraphExtension paragraph:
                    paragraphExtensions.Add(paragraph);
                    break;

                default:
                    throw new ArgumentException($"The syntax extension '{extension.Name}' is not an inline, delimiter, fenced block or paragraph extension.", paramName);
            }
        }

        foreach (var (c, list) in inlineExtensions)
        {
            if (c < 128)
            {
                _asciiInlineExtensions[c] = [.. list];
            }
            else
            {
                (_otherInlineExtensions ??= [])[c] = [.. list];
            }
        }

        foreach (var (c, list) in fencedBlockExtensions)
        {
            if (c < 128)
            {
                _asciiFencedBlockExtensions[c] = [.. list];
            }
            else
            {
                (_otherFencedBlockExtensions ??= [])[c] = [.. list];
            }
        }

        TriggerCharacters = triggers.ToString();
        ParagraphExtensions = paragraphExtensions.Count == 0 ? null : [.. paragraphExtensions];
        HasBlockExtensions = fencedBlockExtensions.Count > 0 || paragraphExtensions.Count > 0;
    }

    /// <summary>Gets the paragraph extensions, in order, or <see langword="null"/> when there is none.</summary>
    public MarkdownParagraphExtension[]? ParagraphExtensions { get; }

    public bool HasBlockExtensions { get; }

    public MarkdownFencedBlockExtension[]? GetFencedBlockExtensions(char c) => c < 128 ? _asciiFencedBlockExtensions[c] : _otherFencedBlockExtensions?.GetValueOrDefault(c);

    public MarkdownSyntaxExtension[] Extensions { get; }

    /// <summary>Gets the trigger characters of the inline extensions, and the characters of the delimiter extensions.</summary>
    public string TriggerCharacters { get; }

    public bool HasDelimiters => Array.Exists(Extensions, extension => extension is MarkdownDelimiterExtension);

    public MarkdownInlineExtension[]? GetInlineExtensions(char c) => c < 128 ? _asciiInlineExtensions[c] : _otherInlineExtensions?.GetValueOrDefault(c);

    /// <summary>Gets the index of the delimiter extension of a character, or -1.</summary>
    public int GetDelimiterIndex(char c) => c < 128 ? _delimiterIndexes[c] : -1;

    public int GetIndex(MarkdownSyntaxExtension extension) => _indexes[extension];

    public bool TryGetIndex(MarkdownSyntaxExtension extension, out int index) => _indexes.TryGetValue(extension, out index);

    public bool IsTrigger(char c) => GetInlineExtensions(c) is not null || GetDelimiterIndex(c) >= 0;
}
