using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Parses the inlines of a leaf block, as the CommonMark reference implementation (commonmark.js) does, except that every
/// construct is a range of the subject: the delimiters, the brackets and the destinations of the links are tokens, not
/// values, so that the tree keeps them.
/// </summary>
internal sealed class InlineParser
{
    private const int MaximumLinkDestinationParentheses = 32;

    // The characters that a link destination without angle brackets stops at, or reads in a special way: the escapes, the
    // parentheses, the spaces and the control characters but NUL, which is U+FFFD for CommonMark
    private static readonly SearchValues<char> LinkDestinationSpecialCharacters = SearchValues.Create("\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F \\()\u007F");

    private readonly Dictionary<string, bool> _references;
    private readonly bool _strikethrough;
    private readonly bool _subscript;
    private readonly bool _superscript;
    private readonly bool _inserted;
    private readonly bool _marked;
    private readonly bool _mathematics;
    private readonly bool _genericAttributes;
    private readonly bool _customContainers;
    private readonly bool _smartyPants;
    private readonly bool _citations;
    private readonly MarkdownEmojiMapping? _emoji;

    // The text before the end of an emoji whose last characters are a delimiter of emphasis, with the length of the emoji: it is
    // an emoji when the emphasis leaves its delimiters as text
    private readonly List<(InlineNode Node, int Length)> _pendingEmojis = [];

    // The quotes of SmartyPants, which are paired at the end of the inlines, where the inlines start, and whether there are dashes
    private readonly List<InlineNode> _smartQuotes = [];
    private int _inlineStart;

    // Whether attributes are valid from each position of the subject, computed at the first {
    private byte[]? _attributesOutcomes;
    private int _attributesOutcomesStart;
    private long _abbreviationExpansion;

    // The labels of the abbreviations, made again when a definition is added
    private AbbreviationMatcher? _abbreviationMatcher;
    private int _abbreviationMatcherLabelCount;
    private string?[] _abbreviationLabels = [];
    private readonly bool _autolinks;
    private readonly MarkdownAutolinkOptions? _markdigAutolinks;
    private readonly MarkdownPipeTableOptions _pipeTableOptions;
    private readonly bool _jiraLinks;
    private readonly bool _cjkFriendly;
    private readonly bool _disableHtml;
    private Subject _subject = null!;
    private string _text = "";

    // Where the subject starts and ends in _text, which is the source when the subject copies nothing
    private int _textStart;
    private int _textEnd;
    private int _pos;
    private int _end;
    private InlineNode _root = null!;

    // The next terminator of the processing instructions, the comments, the CDATA sections and the declarations: see FindHtmlTerminator
    private readonly HtmlTerminator[] _htmlTerminators = new HtmlTerminator[4];

    // The last start of each length of backtick runs seen while looking for the end of a code span, and the end of the subject
    // when a search reached it: an opener whose length has no run after it is text at once, as in cmark
    private readonly Dictionary<int, int> _backtickRuns = [];
    private int _backtickRunsScannedEnd = -1;

    // The number of brackets of _brackets that are active
    private int _activeBrackets;

    // The number of delimiters of _delimiters by ASCII character
    private readonly int[] _pendingDelimiters = new int[128];

    // The last inline of the container that the search for a raw <a> read, and whether one was open there: see IsInRawHtmlAnchor
    private InlineNode? _anchorScanNode;
    private bool _anchorScanIsOpen;

    // The last search for the end of the URL of a Markdig autolink that found unbalanced parentheses: see GetMarkdigUrlEnd
    private MarkdigUrlScan _markdigUrlScan;
    private readonly List<int> _markdigUrlDepths = [];
    private readonly List<int> _markdigUrlLowerDepths = [];

    // The prefixes that a Markdig autolink starts with: the text skips to the next one with a vectorized search instead of
    // stopping at each of their first letters, which are among the most frequent ones
    private static readonly SearchValues<string> MarkdigAutolinkPrefixes = SearchValues.Create(["http://", "https://", "www.", "ftp://", "mailto:", "tel:"], StringComparison.Ordinal);
    private const int MarkdigAutolinkPrefixMaximumLength = 8;

    // The first position of _text, from _markdigAutolinkSearchStart to the end of the subject, where one of these prefixes
    // starts, or int.MaxValue when there is none
    private string? _markdigAutolinkSearchText;
    private int _markdigAutolinkSearchStart;
    private int _markdigAutolinkSearchEnd;
    private int _markdigAutolinkCandidate;

    // The nodes of the inlines, reused from one leaf block to the next
    private readonly List<InlineNode> _nodes = [];
    private int _usedNodes;
    private Delimiter? _delimiters;
    private Bracket? _brackets;

    // Markdig pipe tables: while the inlines of a paragraph are scanned for a table, its pipes are column delimiters. The cells are
    // parsed in the paragraph, so the delimiters of emphasis read the characters around a cell.
    private PipeTableState? _table;
    private bool _scanningPipeTable;
    private int _lookaheadEnd;

    // While a cell of a pipe table is parsed, where it starts: its pipes are text, which Markdig keeps apart from the text around
    private bool _pipeTableCell;
    private int _cellStart;

    /// <summary>
    /// Gets or sets the quotes of SmartyPants of the pipe table whose cells are parsed, by their position, and whether they are
    /// paired in its paragraph. Markdig pairs them before it splits the paragraph in cells.
    /// </summary>
    public IReadOnlyDictionary<int, bool>? PipeTableQuotes { get; set; }

    // The first line break that Markdig adds to an open container, while a paragraph is scanned for a table
    private int _firstNestedLineBreak;

    // The line of the last colon before a pipe, and whether it is a delimiter row under a row
    private int _colonLineStart;
    private bool _colonLineResult;

    // The characters where a run of text can end, for each set of enabled extensions: a superset of the characters that
    // ParseString stops at, so that it can skip the others with a vectorized search
    private const string TextStopCharacters = "\n\\`*_[]!<&|";
    private const int MaximumCachedStopCharacterSets = 64;
    private static readonly ConcurrentDictionary<string, SearchValues<char>> StopCharacterSets = new(StringComparer.Ordinal);

    private readonly SearchValues<char> _textStopCharacters;

    // The syntax extensions of the options, the options that they read, and the parts of the construct that an extension reports
    private readonly SyntaxExtensionSet? _syntaxExtensions;
    private readonly MarkdownParseOptions _options;
    private readonly List<ExtensionRange> _extensionRanges = [];

    /// <summary>Gets or sets the states of the syntax extensions for the document, which the block parser shares.</summary>
    public SyntaxExtensionStates? States { get; set; }

    // The content of the constructs of extensions nested deeper than this is text, so that parsing it cannot exhaust the stack
    private const int MaximumExtensionDepth = 32;
    private int _extensionDepth;

    // The inlines nested deeper than this are text: see FlattenDeepInlines
    private readonly int _maximumNestingDepth;
    private readonly List<InlineNode> _flattenedTokens = [];

    public InlineParser(Dictionary<string, bool> references, MarkdownParseOptions options)
    {
        var extensions = options.Extensions;
        _maximumNestingDepth = options.MaximumNestingDepth;
        _disableHtml = options.DisableHtml;
        _references = references;
        _strikethrough = extensions.HasFlag(MarkdownExtensions.Strikethrough);
        _subscript = extensions.HasFlag(MarkdownExtensions.Subscript);
        _superscript = extensions.HasFlag(MarkdownExtensions.Superscript);
        _inserted = extensions.HasFlag(MarkdownExtensions.Inserted);
        _marked = extensions.HasFlag(MarkdownExtensions.Marked);
        _mathematics = extensions.HasFlag(MarkdownExtensions.Mathematics);
        _genericAttributes = extensions.HasFlag(MarkdownExtensions.GenericAttributes);
        _customContainers = extensions.HasFlag(MarkdownExtensions.CustomContainers);
        _smartyPants = extensions.HasFlag(MarkdownExtensions.SmartyPants);
        _citations = extensions.HasFlag(MarkdownExtensions.Citations);
        _emoji = extensions.HasFlag(MarkdownExtensions.Emoji) ? options.EmojiMapping : null;
        // The autolinks of Markdig replace the ones of GFM
        _autolinks = extensions.HasFlag(MarkdownExtensions.Autolinks) && !extensions.HasFlag(MarkdownExtensions.MarkdigAutolinks);
        _jiraLinks = extensions.HasFlag(MarkdownExtensions.JiraLinks);
        _cjkFriendly = extensions.HasFlag(MarkdownExtensions.CjkFriendlyEmphasis);
        _markdigAutolinks = extensions.HasFlag(MarkdownExtensions.MarkdigAutolinks) ? options.AutolinkOptions : null;
        _pipeTableOptions = options.PipeTableOptions;
        _options = options;
        _syntaxExtensions = options.SyntaxExtensionSet;
        if (_syntaxExtensions is { HasDelimiters: true })
        {
            CheckDelimiterExtensions(_syntaxExtensions, options);
        }

        _textStopCharacters = GetTextStopCharacters();
    }

    // A delimiter extension cannot use a character that an enabled extension reads inline
    private static void CheckDelimiterExtensions(SyntaxExtensionSet syntaxExtensions, MarkdownParseOptions options)
    {
        var extensions = options.Extensions;
        foreach (var extension in syntaxExtensions.Extensions)
        {
            if (extension is not MarkdownDelimiterExtension delimiter)
            {
                continue;
            }

            var conflict = delimiter.Character switch
            {
                '~' => extensions & (MarkdownExtensions.Strikethrough | MarkdownExtensions.Subscript),
                '^' => extensions & MarkdownExtensions.Superscript,
                '+' => extensions & MarkdownExtensions.Inserted,
                '=' => extensions & MarkdownExtensions.Marked,
                ':' => extensions & (MarkdownExtensions.CustomContainers | MarkdownExtensions.Autolinks),
                '"' => extensions & (MarkdownExtensions.Citations | MarkdownExtensions.SmartyPants),
                '\'' or '>' or '.' or '-' => extensions & MarkdownExtensions.SmartyPants,
                '$' => extensions & MarkdownExtensions.Mathematics,
                '{' or '}' => extensions & MarkdownExtensions.GenericAttributes,
                '|' => extensions & (MarkdownExtensions.Tables | MarkdownExtensions.PipeTables | MarkdownExtensions.GridTables),
                _ => MarkdownExtensions.None,
            };

            if (conflict == MarkdownExtensions.None && extensions.HasFlag(MarkdownExtensions.Emoji) && options.EmojiMapping.IsFirstCharacter(delimiter.Character))
            {
                conflict = MarkdownExtensions.Emoji;
            }

            if (conflict != MarkdownExtensions.None)
            {
                throw new ArgumentException($"The delimiter extension '{delimiter.Name}' uses the character '{delimiter.Character}', which {conflict} reads.", nameof(options));
            }
        }
    }

    private SearchValues<char> GetTextStopCharacters()
    {
        var characters = new StringBuilder(TextStopCharacters);
        AppendIf(_strikethrough || _subscript, "~");
        AppendIf(_superscript, "^");
        AppendIf(_inserted, "+");
        AppendIf(_marked, "=");
        AppendIf(_mathematics, "$");
        AppendIf(_genericAttributes, "{");
        AppendIf(_autolinks || _customContainers, ":");
        AppendIf(_autolinks, "w");
        AppendIf(_jiraLinks, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        // The text stops at the prefixes of the Markdig autolinks with another search: see GetNextMarkdigAutolinkCandidate
        AppendIf(_smartyPants, "'\">.");
        AppendIf(_citations, "\"");
        AppendIf(_syntaxExtensions is not null, _syntaxExtensions?.TriggerCharacters ?? "");
        if (_emoji is not null)
        {
            foreach (var c in _emoji.FirstCharacters)
            {
                characters.Append(c);
            }
        }

        var key = characters.ToString();
        if (StopCharacterSets.TryGetValue(key, out var values))
        {
            return values;
        }

        // Custom emoji mappings can make many sets: only the first ones are kept
        values = SearchValues.Create(key);
        if (StopCharacterSets.Count < MaximumCachedStopCharacterSets)
        {
            StopCharacterSets.TryAdd(key, values);
        }

        return values;

        void AppendIf(bool condition, string value)
        {
            if (condition)
            {
                characters.Append(value);
            }
        }
    }

    /// <summary>Parses the inlines of the subject from <paramref name="start"/>, without the whitespace around them, or only before them.</summary>
    public void Parse(Subject subject, int start, TreeBuffer output, bool trimEnd = true) => Parse(subject, start, subject.End, output, trimEnd, inContext: false);

    /// <summary>Parses the inlines of the whole subject.</summary>
    public void Parse(Subject subject, TreeBuffer output, bool trimEnd = true) => Parse(subject, subject.Start, output, trimEnd);

    /// <summary>
    /// Parses the inlines of a range of the subject, a cell of a pipe table, which has no whitespace around it. The delimiters of
    /// emphasis read the characters around the range, as they do in the paragraph of the table.
    /// </summary>
    public void ParseCell(Subject subject, int start, int end, TreeBuffer output) => Parse(subject, start, end, output, trimEnd: false, inContext: true);

    /// <summary>
    /// Parses the inlines of a range of the subject, the lines of a paragraph before a pipe table, without the whitespace around
    /// them. The delimiters of emphasis after <paramref name="splitLineBreak"/> are text, as in Markdig, which moves the inlines
    /// after a line break that is in a delimiter out of it when the table has delimiters in them.
    /// </summary>
    public void ParseBeforePipeTable(Subject subject, int start, int end, int splitLineBreak, TreeBuffer output)
    {
        // The lines can end with a hard line break that is a backslash, which Markdig does not write
        Begin(subject, start, end, trimEnd: end == start || subject.Text[end - 1] != '\n');
        while (_pos < _end)
        {
            ParseInline();
        }

        if (splitLineBreak >= 0)
        {
            for (var delimiter = _delimiters; delimiter is not null; delimiter = delimiter.Previous)
            {
                if (delimiter.Node.Start > splitLineBreak)
                {
                    RemoveDelimiter(delimiter);
                }
            }
        }

        Complete(output);
    }

    private void Parse(Subject subject, int start, int end, TreeBuffer output, bool trimEnd, bool inContext)
    {
        Begin(subject, start, end, trimEnd);
        if (inContext)
        {
            _inlineStart = _textStart;
            _lookaheadEnd = _textEnd;
            _pipeTableCell = true;
            _cellStart = _pos;
        }

        try
        {
            while (_pos < _end)
            {
                ParseInline();
            }
        }
        finally
        {
            _pipeTableCell = false;
        }

        Complete(output);
    }

    // The inlines that the delimiters make once all of them are read, and the tokens of the inlines
    private void Complete(TreeBuffer output)
    {
        ProcessEmphasis(stackBottom: null);
        FlattenDeepInlines();

        // As in Markdig: the dashes after the emphasis, then the emoji that end with a delimiter, then the quotes are paired
        if (_smartyPants)
        {
            MarkDashes(_root);
        }

        ResolvePendingEmojis();
        if (_smartyPants)
        {
            if (PipeTableQuotes is null)
            {
                PairSmartQuotes();
            }
            else
            {
                // The quotes of a pipe table and of the lines before it are paired in the paragraph, as Markdig does
                foreach (var quote in _smartQuotes)
                {
                    if (PipeTableQuotes.TryGetValue(quote.Start, out var paired) && !paired)
                    {
                        ToSmartyPantText(quote);
                    }
                }
            }
        }

        if (_genericAttributes)
        {
            DetachAttributes(_root);
        }

        if (_autolinks)
        {
            LinkEmailAddresses(_root);
        }

        if (Abbreviations.Count > 0)
        {
            MarkAbbreviations(_root);
        }

        // The email addresses and the abbreviations are inlines around the tokens of an inline, which can be at the limit
        if (_autolinks || Abbreviations.Count > 0)
        {
            FlattenDeepInlines();
        }

        for (var child = _root.FirstChild; child is not null; child = child.Next)
        {
            AddBuildElements(child, output);
        }
    }

    /// <summary>
    /// Reads the inlines of the subject from <paramref name="start"/> as the pipe table parser of Markdig does, to find a table:
    /// the lines that have a pipe, from the first one, whose second line is a delimiter row. Returns <see langword="null"/> when
    /// there is none, and adds the inlines of the paragraph to <paramref name="paragraph"/>. Else the inlines are not kept: the
    /// cells, and the lines around the table, are parsed afterwards.
    /// </summary>
    public PipeTableScan? ScanPipeTable(Subject subject, int start, TreeBuffer paragraph, bool trimEnd = true)
    {
        Begin(subject, start, subject.End, trimEnd);
        _scanningPipeTable = true;
        _table = null;
        _colonLineStart = -1;
        _firstNestedLineBreak = -1;
        PipeTableScan? scan;
        try
        {
            while (_pos < _end)
            {
                ParseInline();
            }

            scan = CompletePipeTable();
        }
        finally
        {
            _scanningPipeTable = false;
        }

        _table = null;
        if (scan is not null && _smartQuotes.Count > 0)
        {
            PairSmartQuotes();
            var quotes = new Dictionary<int, bool>(_smartQuotes.Count);
            foreach (var quote in _smartQuotes)
            {
                quotes[quote.Start] = quote.Kind == SyntaxKind.SmartyPantToken;
            }

            scan.Quotes = quotes;
        }

        if (scan is null)
        {
            // The paragraph is not a table: its pipes are text, and its inlines are the ones Markdig reads with the pipes
            for (var child = _root.FirstChild; child is not null; child = child.Next)
            {
                if (child.Kind == SyntaxKind.PipeToken)
                {
                    // A text apart from the text around it, as Markdig keeps it
                    child.ChangeKind(SyntaxKind.TextToken);
                    child.Value = 0;
                }
            }

            Complete(paragraph);
        }

        return scan;
    }

    // Whether Markdig has a container that is open: a delimiter of emphasis or a bracket, in which the next inlines are added
    private bool HasOpenContainer => _delimiters is not null || _brackets is not null;

    // A pipe is a column delimiter, unless it is in a link
    private void ParsePipe()
    {
        _table ??= new PipeTableState();
        _table.LineHasPipe = true;
        var pipe = NewNode(SyntaxKind.PipeToken, _pos, _pos + 1);
        pipe.IsDelimiterRun = true;
        if (HasOpenContainer)
        {
            _table.HasNestedDelimiters = true;
        }
        else
        {
            _table.HasRootPipe = true;
        }

        _root.Append(pipe);
        _pos++;
        _table.AddDelimiter(pipe);
    }

    // A line ends the table when it has no pipe. When the next line cannot have one, the table ends before it, and the parse stops
    // there when the lines before it are a table: the lines after it are another paragraph.
    private void ParsePipeTableNewline()
    {
        var isFirstLineEmpty = false;
        if (_table is null)
        {
            if (HasInlines())
            {
                ParseNewline();
                return;
            }

            isFirstLineEmpty = true;
            _table = new PipeTableState();
        }

        var table = _table;
        if (!isFirstLineEmpty && !table.LineHasPipe)
        {
            if (table.EndOfLines.Count == 0)
            {
                table.IsInvalid = true;
            }
            else if (table.EndOfTable is null && !table.IsInvalid)
            {
                table.EndOfTable = table.EndOfLines[^1];
                table.EndOfTableLineStart = table.LineStart;
                if (EndPipeTable())
                {
                    _pos = _end;
                    return;
                }
            }
        }

        table.LineHasPipe = false;
        ParseNewline();
        if (isFirstLineEmpty)
        {
            return;
        }

        var lineBreak = _root.LastChild!;
        table.HasNestedDelimiters |= HasOpenContainer;
        table.AddDelimiter(lineBreak);
        table.EndOfLines.Add(lineBreak);
        table.LineStart = _pos;
        if (table.EndOfTable is null && !table.IsInvalid && !CanHaveColumnDelimiter(_pos))
        {
            table.EndOfTable = lineBreak;
            table.EndOfTableLineStart = _pos;
            if (EndPipeTable())
            {
                _pos = _end;
            }
        }
    }

    // Whether inlines were read, other than generic attributes, which Markdig does not add
    private bool HasInlines()
    {
        for (var child = _root.FirstChild; child is not null; child = child.Next)
        {
            if (child.Kind != SyntaxKind.GenericAttributes)
            {
                return true;
            }
        }

        return false;
    }

    // Whether the line has a pipe that a backslash does not escape: the pipe of a code span is found when its line is parsed
    private bool CanHaveColumnDelimiter(int position)
    {
        for (var i = position; i < _end; i++)
        {
            switch (_text[i])
            {
                case '\n':
                    return false;
                case '\\' when i + 1 < _end && Scanners.IsEscapable(_text[i + 1]):
                    i++;
                    break;
                case '|':
                    return true;
            }
        }

        return false;
    }

    // The lines before the end of the table are a table when they have a delimiter row, a header row that is not a single pipe,
    // and when the line break that ends them is not in a link
    private bool EndPipeTable()
    {
        var table = _table!;
        var delimiters = table.Delimiters;
        if (FindPipeTableColumns(delimiters, delimiters.LastIndexOf(table.EndOfTable!) + 1) is null || IsHeaderWithoutCell(delimiters) || table.EndOfTable!.Parent != _root)
        {
            table.IsInvalid = true;
            return false;
        }

        table.IsParsingStopped = true;
        return true;
    }

    // Whether Markdig finds a column delimiter from the last inline: it is one, or a pipe is a child of the paragraph
    private bool HasPipeForMarkdig() => _table is not null && (_table.HasRootPipe || _root.LastChild is { Kind: SyntaxKind.PipeToken });

    // Markdig replaces a bracket that does not open a link with text, which makes the pipes after it children of the paragraph
    // when no other container is open
    private void PromotePipesOfBracket(Bracket opener)
    {
        if (_table is null || _table.HasRootPipe || HasOpenContainer)
        {
            return;
        }

        for (var node = opener.Node.Next; node is not null; node = node.Next)
        {
            if (node.Kind == SyntaxKind.PipeToken)
            {
                _table.HasRootPipe = true;
                return;
            }
        }
    }

    // The pipes of a link are text
    private void RemovePipesOfLink(InlineNode link)
    {
        List<InlineNode>? pipes = null;
        for (var child = link.LastChild; child is not null; child = child.Previous)
        {
            if (child.Kind == SyntaxKind.PipeToken)
            {
                (pipes ??= []).Add(child);
            }
        }

        if (pipes is null)
        {
            return;
        }

        if (!_table!.RemovePipesOfClosedInline(pipes))
        {
            _table.LineHasPipe = false;
        }

        // A text apart from the text around it, as Markdig keeps it
        foreach (var pipe in pipes)
        {
            pipe.ChangeKind(SyntaxKind.TextToken);
            pipe.Value = 0;
        }
    }

    // A colon before a pipe of a delimiter row under a line that has a pipe is text, so that no other inline reads it
    private bool IsDelimiterRowColonBeforePipe()
    {
        if (_table is null || CharAt(_pos + 1) != '|')
        {
            return false;
        }

        var lineStart = _textStart + _text.AsSpan(_textStart, _pos - _textStart).LastIndexOf('\n') + 1;
        if (lineStart == _colonLineStart)
        {
            return _colonLineResult;
        }

        var lineEnd = _text.AsSpan(_pos, _textEnd - _pos).IndexOf('\n') is >= 0 and var length ? _pos + length : _textEnd;

        var result = lineStart > _textStart;
        var hasPipe = false;
        var hasDash = false;
        for (var i = lineStart; i < lineEnd && result; i++)
        {
            switch (_text[i])
            {
                case '|':
                    hasPipe = true;
                    break;
                case '-':
                    hasDash = true;
                    break;
                case not (':' or ' ' or '\t'):
                    result = false;
                    break;
            }
        }

        if (result && hasPipe && hasDash)
        {
            var previousLineStart = _textStart + _text.AsSpan(_textStart, lineStart - 1 - _textStart).LastIndexOf('\n') + 1;
            result = _text.AsSpan(previousLineStart, lineStart - 1 - previousLineStart).Contains('|');
        }
        else
        {
            result = false;
        }

        _colonLineStart = lineStart;
        _colonLineResult = result;
        return result;
    }

    private PipeTableScan? CompletePipeTable()
    {
        var table = _table;
        if (table is null || table.IsInvalid)
        {
            return null;
        }

        // The table ends before the first line without a pipe, which starts a paragraph after the table
        var last = table.EndOfTable;
        var trailingStart = table.EndOfTableLineStart;
        if (last is null && !table.LineHasPipe)
        {
            if (table.EndOfLines.Count == 0)
            {
                return null;
            }

            last = table.EndOfLines[^1];
            trailingStart = table.LineStart;
        }

        var delimiters = table.Delimiters;
        var count = last is null ? delimiters.Count : delimiters.IndexOf(last) + 1;
        var hasDelimiterRow = true;
        if (!table.IsParsingStopped)
        {
            // Without a delimiter row, the lines are a table without a header when it is allowed and they end the paragraph
            // and they have a pipe that is not in a link (Markdig 1.x throws on lines whose pipes are all in links)
            hasDelimiterRow = FindPipeTableColumns(delimiters, count) is not null;
            if ((!hasDelimiterRow && (_pipeTableOptions.RequireHeaderSeparator || last is not null || !HasPipe(delimiters, count))) || (last is not null && last.Parent != _root) || (hasDelimiterRow && IsHeaderWithoutCell(delimiters)))
            {
                return null;
            }
        }

        var scan = new PipeTableScan { TrailingStart = last is null ? -1 : trailingStart, SplitLineBreak = table.HasNestedDelimiters ? _firstNestedLineBreak : -1, HasDelimiterRow = hasDelimiterRow };
        var rowStart = GetRowStart(delimiters[0], out var leadingEnd);
        scan.LeadingEnd = leadingEnd;
        var pipes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var delimiter = delimiters[i];
            if (delimiter.IsDelimiterRun)
            {
                pipes.Add(delimiter.Start);
                continue;
            }

            scan.Rows.Add(CreatePipeTableRow(rowStart, delimiter.Start, pipes, delimiter, isDelimiterRow: hasDelimiterRow && scan.Rows.Count == 1));
            pipes.Clear();
            rowStart = delimiter.End;
        }

        if (count > 0 && delimiters[count - 1].IsDelimiterRun)
        {
            scan.Rows.Add(CreatePipeTableRow(rowStart, _end, pipes, _root, isDelimiterRow: hasDelimiterRow && scan.Rows.Count == 1));
        }
        else if (last is null && !_text.AsSpan(rowStart, _end - rowStart).Trim(" \t\n\v\f\r").IsEmpty)
        {
            // The pipes of the last line were in a link: Markdig counts the line as a row without cell, and writes its text in a
            // paragraph before the table. It is a paragraph after the table, where the text is.
            scan.TrailingStart = rowStart;
        }

        return scan;
    }

    private static bool HasPipe(List<InlineNode> delimiters, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (delimiters[i].IsDelimiterRun)
            {
                return true;
            }
        }

        return false;
    }

    // A pipe that only whitespace precedes starts the row, and the text after the last pipe is a cell when it is not whitespace.
    // Markdig does not add the generic attributes to the inlines: they are whitespace there, and the ones that are not in a cell
    // are the attributes of the row. rowEnd: the line break after the row, or the root when it is the last line. The cells of
    // the delimiter row are the ones that are not empty, without its attributes.
    private PipeTableScanRow CreatePipeTableRow(int start, int end, List<int> pipes, InlineNode rowEnd, bool isDelimiterRow)
    {
        var row = new PipeTableScanRow { Start = start, End = end };
        row.Pipes.AddRange(pipes);
        List<(int Start, int End)>? attributes = null;
        for (var node = rowEnd == _root ? _root.LastChild : rowEnd.Previous; node is not null && node.Kind is not (SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken); node = node.Previous)
        {
            // An inline that spans lines, such as a link, can be before the row's line ending: the attributes before it are not in the row
            if (node.Kind == SyntaxKind.GenericAttributes && node.Start >= start && node.End <= end)
            {
                (attributes ??= []).Insert(0, (node.Start, node.End));
            }
        }

        // The text of the row, where the attributes are whitespace, is copied from the start of the row, which is at textOffset in
        // the copy: the text of a subject can be the whole source
        var text = _text;
        var textOffset = 0;
        if (attributes is not null)
        {
            var characters = _text.ToCharArray(start, end - start);
            foreach (var (attributesStart, attributesEnd) in attributes)
            {
                if (isDelimiterRow)
                {
                    PipeTableScanner.BlankAttributes(characters, 0, attributesStart - start, attributesEnd - start);
                }
                else
                {
                    characters.AsSpan(attributesStart - start, attributesEnd - attributesStart).Fill(' ');
                }
            }

            text = new string(characters);
            textOffset = start;
            if (isDelimiterRow)
            {
                row.Attributes.AddRange(attributes);
            }
        }

        var cellStart = start;
        for (var i = 0; i < pipes.Count; i++)
        {
            if (i == 0 && PipeTableScanner.IsWhitespace(text, start - textOffset, pipes[i] - textOffset))
            {
                AddRowAttributes(start, pipes[i]);
                cellStart = pipes[i] + 1;
                continue;
            }

            AddCell(cellStart, pipes[i]);
            cellStart = pipes[i] + 1;
        }

        if (!PipeTableScanner.IsWhitespace(text, cellStart - textOffset, end - textOffset))
        {
            AddCell(cellStart, end);
        }
        else
        {
            AddRowAttributes(cellStart, end);
        }

        return row;

        void AddRowAttributes(int segmentStart, int segmentEnd)
        {
            if (attributes is null || isDelimiterRow)
            {
                return;
            }

            foreach (var range in attributes)
            {
                if (range.Start >= segmentStart && range.End <= segmentEnd)
                {
                    row.Attributes.Add(range);
                }
            }
        }

        void AddCell(int cellStart, int cellEnd)
        {
            // A hard line break at the end of the cell is in the cell: the cell starts after it, as in Markdig
            var cell = isDelimiterRow ? TrimRowText(cellStart, cellEnd) : PipeTableScanner.Trim(_text, cellStart, cellEnd);
            if (cell.End < cellEnd && _text[cell.End] == '\n' && cell.End > cell.Start && _text[cell.End - 1] == '\\')
            {
                cell.End++;
            }

            if (!isDelimiterRow)
            {
                row.Cells.Add(cell);
                return;
            }

            // The cells of the delimiter row are tokens, which the attributes split
            var position = cell.Start;
            foreach (var (attributesStart, attributesEnd) in row.Attributes)
            {
                if (attributesEnd <= position || attributesStart >= cell.End)
                {
                    continue;
                }

                AddDelimiterToken(position, attributesStart);
                position = attributesEnd;
            }

            AddDelimiterToken(position, cell.End);
        }

        void AddDelimiterToken(int tokenStart, int tokenEnd)
        {
            var token = TrimRowText(tokenStart, tokenEnd);
            if (token.End > token.Start)
            {
                row.Cells.Add(token);
            }
        }

        (int Start, int End) TrimRowText(int trimStart, int trimEnd)
        {
            var (trimmedStart, trimmedEnd) = PipeTableScanner.Trim(text, trimStart - textOffset, trimEnd - textOffset);
            return (trimmedStart + textOffset, trimmedEnd + textOffset);
        }
    }

    // Where the row of a delimiter starts: after the line break before it, or where the inlines start. lineBreakStart: where the
    // line break starts, which ends the lines before the row.
    private int GetRowStart(InlineNode delimiter, out int lineBreakStart)
    {
        for (var node = delimiter.Previous; node is not null; node = node.Previous)
        {
            if (node.Kind is SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken)
            {
                // The lines before the row end with a hard line break that is a backslash, which is not whitespace
                lineBreakStart = _text[node.Start] == '\\' ? node.End : node.Start;
                return node.End;
            }
        }

        lineBreakStart = _inlineStart;
        return _inlineStart;
    }

    private static bool IsLineBreak(InlineNode node) => !node.IsDelimiterRun;

    private bool IsOpenBracket(InlineNode node)
    {
        for (var bracket = _brackets; bracket is not null; bracket = bracket.Previous)
        {
            if (bracket.Node == node)
            {
                return true;
            }
        }

        return false;
    }

    // A header row that is a single pipe has no cell. Markdig only looks at the inlines before the pipe in its container: the
    // inlines after a delimiter of emphasis or a bracket that is not closed are in it.
    private bool IsHeaderWithoutCell(List<InlineNode> delimiters)
    {
        if (delimiters is not [{ IsDelimiterRun: true } pipe, { IsDelimiterRun: false } lineBreak, ..])
        {
            return false;
        }

        // The generic attributes are not inlines
        for (var node = pipe.Next; node is not null && node != lineBreak; node = node.Next)
        {
            if (node.Kind != SyntaxKind.GenericAttributes && (!node.IsToken || !PipeTableScanner.IsWhitespace(_text, node.Start, node.End)))
            {
                return false;
            }
        }

        for (var node = pipe.Previous; node is not null; node = node.Previous)
        {
            if (node.Kind == SyntaxKind.GenericAttributes)
            {
                continue;
            }

            if (node.Kind is SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken || (node.Kind == SyntaxKind.TextToken && node.IsDelimiterRun) || IsOpenBracket(node))
            {
                return true;
            }

            if (!PipeTableScanner.IsWhitespace(_text, node.Start, node.End) || !node.IsToken)
            {
                return false;
            }
        }

        return true;
    }

    // The columns of the delimiter row, the line after the first line break of the table, which must only have pipes, dashes,
    // colons and whitespace. Count: the number of delimiters of the table, the ones after them are in the lines after the table.
    private List<(MarkdownTableAlignment Alignment, int DashCount)>? FindPipeTableColumns(List<InlineNode> delimiters, int count)
    {
        var lineBreakIndex = 0;
        while (lineBreakIndex < count && !IsLineBreak(delimiters[lineBreakIndex]))
        {
            lineBreakIndex++;
        }

        if (lineBreakIndex >= count)
        {
            return null;
        }

        // The generic attributes of the row are for the table: Markdig does not add them to the inlines
        // The copies of the line start at the line, which is at lineStart in them: the text of a subject can be the whole source
        var lineStart = delimiters[lineBreakIndex].End;
        char[]? withoutAttributes = null;
        char[]? columnsText = null;
        for (var node = delimiters[lineBreakIndex].Next; node is not null && node.Kind is not (SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken); node = node.Next)
        {
            if (node.Kind == SyntaxKind.GenericAttributes)
            {
                // The text of the row must be the one of a delimiter row, and the columns are read after the attributes
                withoutAttributes ??= _text.ToCharArray(lineStart, _end - lineStart);
                withoutAttributes.AsSpan(node.Start - lineStart, node.End - node.Start).Fill(' ');
                (columnsText ??= _text.ToCharArray(lineStart, _end - lineStart)).AsSpan(node.Start - lineStart, node.End - node.Start).Fill(' ');
                PipeTableScanner.BlankAttributes(columnsText, 0, node.Start - lineStart, node.End - lineStart);
            }
            else if (node.Kind is not (SyntaxKind.PipeToken or SyntaxKind.TextToken or SyntaxKind.EscapedCharacterToken) || (node.Kind == SyntaxKind.TextToken && node.IsDelimiterRun))
            {
                return null;
            }
        }

        // Attributes can span lines: the row ends at the first line ending that is not in attributes
        var (text, textOffset) = withoutAttributes is null ? (_text, 0) : (new string(withoutAttributes), lineStart);
        var lineEnd = text.AsSpan(lineStart - textOffset, _end - lineStart).IndexOf('\n') is >= 0 and var length ? lineStart + length : _end;
        if (!PipeTableScanner.IsDelimiterRowText(text, lineStart - textOffset, lineEnd - textOffset))
        {
            return null;
        }

        var pipes = new List<int>();
        var index = lineBreakIndex + 1;
        while (index < count && delimiters[index].IsDelimiterRun)
        {
            pipes.Add(delimiters[index].Start);
            index++;
        }

        var hasLineEnd = index < count;
        var columnsEnd = hasLineEnd ? delimiters[index].Start : lineEnd;

        // The copies are made together: without attributes, the columns are read in the text
        return columnsText is null
            ? PipeTableScanner.ReadColumns(_text, lineStart, columnsEnd, pipes, hasLineEnd)
            : PipeTableScanner.ReadColumns(new string(columnsText), 0, columnsEnd - lineStart, pipes.ConvertAll(pipe => pipe - lineStart), hasLineEnd);
    }

    private void Begin(Subject subject, int start, int end, bool trimEnd)
    {
        _subject = subject;
        _text = subject.Text;
        _textStart = subject.Start;
        _textEnd = subject.End;
        _end = end;
        while (trimEnd && _end > start && _text[_end - 1] is ' ' or '\t' or '\n')
        {
            _end--;
        }

        _lookaheadEnd = _end;
        _pos = start;
        while (_pos < _end && _text[_pos] is ' ' or '\t' or '\n')
        {
            _pos++;
        }

        _usedNodes = 0;
        _inlineStart = _pos;
        _smartQuotes.Clear();
        _pendingEmojis.Clear();
        _attributesOutcomes = null;
        _root = NewNode(SyntaxKind.Paragraph, _pos, _pos);
        _delimiters = null;
        _brackets = null;
        _activeBrackets = 0;
        Array.Clear(_pendingDelimiters);
        _backtickRuns.Clear();
        _backtickRunsScannedEnd = -1;
        _anchorScanNode = null;
        _anchorScanIsOpen = false;
        _markdigUrlScan = default;
    }

    // Parses a link reference definition at the start of the subject. Returns where it ends, or -1 when there is none.
    /// <summary>
    /// Parses a link reference definition at the start of the subject, which is added to <paramref name="definitions"/>. Returns
    /// where it ends, or -1 when there is none.
    /// </summary>
    public int ParseReference(Subject subject, int start, ref List<BuildElement>? definitions)
    {
        _subject = subject;
        _text = subject.Text;
        _textStart = subject.Start;
        _textEnd = subject.End;
        _end = subject.End;
        _pos = start;

        var labelLength = ScanLinkLabel();
        if (labelLength == 0)
        {
            return -1;
        }

        var labelEnd = _pos + labelLength;
        var rawLabel = _text[(_pos + 1)..(labelEnd - 1)];
        _pos = labelEnd;
        if (Peek() != ':')
        {
            return -1;
        }

        var colon = _pos;
        _pos++;
        SkipSpacesAndNewline();
        var destinationStart = _pos;
        if (!ScanLinkDestination())
        {
            return -1;
        }

        var destinationEnd = _pos;
        var beforeTitle = _pos;
        SkipSpacesAndNewline();
        var titleStart = -1;
        var titleEnd = -1;
        if (_pos != beforeTitle && ScanLinkTitle() is > 0 and var titleLength)
        {
            titleStart = _pos;
            titleEnd = _pos + titleLength;
            _pos = titleEnd;
        }
        else
        {
            _pos = beforeTitle;
        }

        // The definition must end its line: else the title is not part of it
        if (!IsAtLineEnd())
        {
            if (titleStart < 0)
            {
                return -1;
            }

            titleStart = -1;
            _pos = beforeTitle;
            if (!IsAtLineEnd())
            {
                return -1;
            }
        }

        var normalized = Scanners.NormalizeLabel(rawLabel);
        if (normalized.Length == 0)
        {
            return -1;
        }

        _references.TryAdd(normalized, value: true);

        definitions ??= [];
        var definition = definitions.Count;
        definitions.Add(BuildElement.Node(SyntaxKind.LinkReferenceDefinition, 0));
        AddRange(definitions, SyntaxKind.OpenBracketToken, start, start + 1);
        AddRange(definitions, SyntaxKind.LinkLabelToken, start + 1, labelEnd - 1);
        AddRange(definitions, SyntaxKind.CloseBracketToken, labelEnd - 1, labelEnd);
        AddRange(definitions, SyntaxKind.ColonToken, colon, colon + 1);
        AddRange(definitions, SyntaxKind.LinkDestinationToken, destinationStart, destinationEnd);
        if (titleStart >= 0)
        {
            AddRange(definitions, SyntaxKind.LinkTitleToken, titleStart, titleEnd);
        }

        definitions[definition] = BuildElement.Node(SyntaxKind.LinkReferenceDefinition, definitions.Count - definition - 1);

        // The end of the line is part of the definition
        while (_pos < _end && _text[_pos] is ' ' or '\t')
        {
            _pos++;
        }

        if (_pos < _end && _text[_pos] == '\n')
        {
            _pos++;
        }

        return _pos;
    }

    // Only spaces or tabs follow on the line
    private bool IsAtLineEnd()
    {
        var position = _pos;
        while (position < _end && _text[position] is ' ' or '\t')
        {
            position++;
        }

        return position == _end || _text[position] == '\n';
    }

    private char Peek() => _pos < _end ? _text[_pos] : '\0';

    private bool PeekIs(char c) => _pos < _end && _text[_pos] == c;

    private void ParseInline()
    {
        var c = _text[_pos];

        // Markdig reads the pipes of a table, and the colons before them in a delimiter row, before the other inlines
        if (_scanningPipeTable)
        {
            if (c == '|')
            {
                ParsePipe();
                return;
            }

            if (c == ':' && IsDelimiterRowColonBeforePipe())
            {
                AppendText(_pos, _pos + 1);
                _pos++;
                return;
            }
        }
        else if (_pipeTableCell && c == '|')
        {
            AppendText(_pos, _pos + 1).IsDelimiterRun = true;
            _pos++;
            return;
        }

        // The syntax extensions are read before the built-in syntaxes
        if (_syntaxExtensions is not null)
        {
            if (_syntaxExtensions.GetInlineExtensions(c) is { } inlineExtensions && ParseInlineExtension(inlineExtensions))
            {
                return;
            }

            if (_syntaxExtensions.GetDelimiterIndex(c) >= 0)
            {
                HandleDelimiter(c);
                return;
            }
        }

        // Markdig reads the emoji before the other inlines, except the escapes and the links
        // Markdig reads its autolinks first
        if (_markdigAutolinks is not null && c is 'h' or 'f' or 'm' or 't' or 'w' && IsMarkdigAutolinkStart(_pos) && ParseMarkdigAutolink())
        {
            return;
        }

        if (_emoji is not null && c != ']' && _emoji.IsFirstCharacter(c) && ParseEmoji())
        {
            return;
        }

        switch (c)
        {
            case '\n':
                if (_scanningPipeTable)
                {
                    ParsePipeTableNewline();
                }
                else
                {
                    ParseNewline();
                }

                break;
            case '\\':
                ParseBackslash();
                break;
            case '`':
                ParseBackticks();
                break;
            case '*' or '_':
                HandleDelimiter(c);
                break;
            case '~' when _strikethrough || _subscript:
                HandleDelimiter(c);
                break;
            case '^' when _superscript:
                HandleDelimiter(c);
                break;
            case '+' when _inserted:
                HandleDelimiter(c);
                break;
            case '=' when _marked:
                HandleDelimiter(c);
                break;
            case '{' when _genericAttributes:
                if (!ParseAttributes())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '$' when _mathematics:
                if (!ParseMath())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case 'w' when _autolinks:
                if (!ParseWwwAutolink())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case ':' when _autolinks || _customContainers:
                if (_autolinks && ParseUrlAutolink())
                {
                    break;
                }

                if (_customContainers)
                {
                    HandleDelimiter(c);
                }
                else
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '[':
                ParseOpenBracket(image: false);
                break;
            case '!':
                if (_pos + 1 < _end && _text[_pos + 1] == '[')
                {
                    ParseOpenBracket(image: true);
                }
                else
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case ']':
                ParseCloseBracket();
                break;
            case >= 'A' and <= 'Z' when _jiraLinks && IsJiraLinkStart(_pos):
                if (!ParseJiraLink())
                {
                    ParseString();
                }

                break;
            case '"' when _citations && CharAt(_pos + 1) == '"':
                HandleDelimiter(c);
                break;
            case '\'' or '"' or '>' or '.' when _smartyPants:
                if (!ParseSmartyPant())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '<':
                if (!(_smartyPants && ParseSmartyPant()) && !ParseAutolink() && (_disableHtml || !ParseHtmlTag()))
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            case '&':
                if (!ParseEntity())
                {
                    AppendText(_pos, _pos + 1);
                    _pos++;
                }

                break;
            default:
                ParseString();
                break;
        }
    }

    // Text: the first character, which can be one where an extension did not start, then up to the next special character
    private void ParseString()
    {
        var start = _pos;
        _pos++;
        if (_markdigAutolinks is not null)
        {
            ParseStringWithMarkdigAutolinks();
        }
        else
        {
            while (_pos < _end)
            {
                // Skip the characters that cannot end the text, then check the one found
                var index = _text.AsSpan(_pos, _end - _pos).IndexOfAny(_textStopCharacters);
                if (index < 0)
                {
                    _pos = _end;
                    break;
                }

                _pos += index;
                if (IsTextStop(_text[_pos]))
                {
                    break;
                }

                _pos++;
            }
        }

        AppendText(start, _pos);
    }

    // The text up to the next special character, or up to the next autolink of Markdig, whose prefixes are found by a
    // search of their own
    private void ParseStringWithMarkdigAutolinks()
    {
        var candidate = GetNextMarkdigAutolinkCandidate(_pos);
        while (_pos < _end)
        {
            var limit = Math.Min(_end, candidate);
            var index = _text.AsSpan(_pos, limit - _pos).IndexOfAny(_textStopCharacters);
            if (index >= 0)
            {
                _pos += index;
                if (IsTextStop(_text[_pos]))
                {
                    break;
                }

                _pos++;
                continue;
            }

            _pos = limit;
            if (limit == _end || IsMarkdigAutolinkStart(_pos))
            {
                break;
            }

            _pos++;
            candidate = GetNextMarkdigAutolinkCandidate(_pos);
        }
    }

    private bool IsTextStop(char c) => c is '\n' or '\\' or '`' or '*' or '_' or '[' or ']' or '!' or '<' or '&' || IsExtensionCharacter(c) || ((_scanningPipeTable || _pipeTableCell) && c == '|');

    // The first position from position to the end of the subject where a prefix of a Markdig autolink starts, or
    // int.MaxValue. The text is read forward: the result of the last search is reused until the position passes it.
    private int GetNextMarkdigAutolinkCandidate(int position)
    {
        if (ReferenceEquals(_markdigAutolinkSearchText, _text) && _markdigAutolinkSearchEnd == _textEnd)
        {
            if (position >= _markdigAutolinkSearchStart && position <= _markdigAutolinkCandidate)
            {
                return _markdigAutolinkCandidate;
            }

            if (position < _markdigAutolinkSearchStart)
            {
                // Back before the last search: only the characters before it are read, with the ones a prefix can overlap
                var length = Math.Min(_textEnd, _markdigAutolinkSearchStart + MarkdigAutolinkPrefixMaximumLength - 1) - position;
                var index = _text.AsSpan(position, length).IndexOfAny(MarkdigAutolinkPrefixes);
                if (index >= 0 && position + index < _markdigAutolinkSearchStart)
                {
                    _markdigAutolinkCandidate = position + index;
                }

                _markdigAutolinkSearchStart = position;
                return _markdigAutolinkCandidate;
            }
        }

        _markdigAutolinkSearchText = _text;
        _markdigAutolinkSearchEnd = _textEnd;
        _markdigAutolinkSearchStart = position;
        var found = position < _textEnd ? _text.AsSpan(position, _textEnd - position).IndexOfAny(MarkdigAutolinkPrefixes) : -1;
        _markdigAutolinkCandidate = found < 0 ? int.MaxValue : position + found;
        return _markdigAutolinkCandidate;
    }

    // An autolink of Markdig starts with a scheme or www., after whitespace or a valid previous character
    private bool IsMarkdigAutolinkStart(int position)
    {
        var previous = position > _inlineStart ? _text[position - 1] : '\0';
        if (previous != '\0' && !Scanners.IsUnicodeWhitespace(GetRuneBefore(position)) && !_markdigAutolinks!.ValidPreviousCharacters.Contains(previous, StringComparison.Ordinal))
        {
            return false;
        }

        var text = _text.AsSpan(position, _end - position);
        return text.Length >= 4 && text[0] switch
        {
            'h' => text.StartsWith("https://", StringComparison.Ordinal) || text.StartsWith("http://", StringComparison.Ordinal),
            'w' => text.StartsWith("www.", StringComparison.Ordinal),
            'f' => text.StartsWith("ftp://", StringComparison.Ordinal),
            'm' => text.StartsWith("mailto:", StringComparison.Ordinal),
            _ => text.StartsWith("tel:", StringComparison.Ordinal),
        };
    }

    // An autolink of Markdig: the URL up to whitespace, a control character, <, an entity, or trailing punctuation, whose
    // parentheses are balanced, without the characters of the delimiters of emphasis that are open
    private bool ParseMarkdigAutolink()
    {
        var start = _pos;

        // Not in a link, or after an <a> that is not closed
        if (_activeBrackets > 0 || IsInRawHtmlAnchor())
        {
            return false;
        }

        var urlEnd = GetMarkdigUrlEnd(start);
        if (urlEnd < 0)
        {
            return false;
        }

        var end = urlEnd;
        var trimmed = urlEnd;
        while (trimmed > start && IsPendingDelimiter(_text[trimmed - 1]))
        {
            trimmed--;
        }

        if (trimmed > start)
        {
            end = trimmed;
        }

        var link = _text.AsSpan(start, end - start);
        int domainOffset;
        switch (link[0])
        {
            case 'h':
                if (link is "http://" or "https://")
                {
                    return false;
                }

                domainOffset = link[4] == 's' ? 8 : 7;
                break;
            case 'w':
                domainOffset = 4;
                break;
            case 'f':
                if (link is "ftp://")
                {
                    return false;
                }

                domainOffset = 6;
                break;
            case 't':
                if (link.Length <= 4)
                {
                    return false;
                }

                domainOffset = -1;
                break;
            default:
                var at = link.IndexOf('@');
                if (at is -1 or 7)
                {
                    return false;
                }

                domainOffset = at + 1;
                break;
        }

        if (domainOffset >= 0 && !IsValidMarkdigDomain(link, domainOffset, _markdigAutolinks!.AllowDomainWithoutPeriod))
        {
            return false;
        }

        var node = NewNode(SyntaxKind.MarkdigAutolink, start, end);
        node.Append(NewNode(SyntaxKind.ExtendedAutolinkToken, start, end));
        _root.Append(node);
        _pos = end;
        return true;

        bool IsPendingDelimiter(char c)
        {
            if (c < _pendingDelimiters.Length)
            {
                return _pendingDelimiters[c] > 0;
            }

            for (var delimiter = _delimiters; delimiter is not null; delimiter = delimiter.Previous)
            {
                if (delimiter.Character == c)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Whether a raw <a> that is not closed is before the position, in the inlines of the container. The inlines read before are
    // not read again: a node of the container can only move into a link, which needs an active bracket before it, and no
    // autolink is read while one is open.
    private bool IsInRawHtmlAnchor()
    {
        var stop = _anchorScanNode?.Parent == _root ? _anchorScanNode : null;
        var isOpen = stop is not null && _anchorScanIsOpen;
        for (var previous = _root.LastChild; previous is not null && previous != stop; previous = previous.Previous)
        {
            if (previous.Kind == SyntaxKind.RawHtml)
            {
                var html = _text.AsSpan(previous.Start, previous.End - previous.Start);
                if (html.StartsWith("</a", StringComparison.OrdinalIgnoreCase))
                {
                    isOpen = false;
                    break;
                }

                if (html.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
                {
                    isOpen = true;
                    break;
                }
            }
        }

        _anchorScanNode = _root.LastChild;
        _anchorScanIsOpen = isOpen;
        return isOpen;
    }

    // Where the URL of a Markdig autolink ends: at the first ) that closes no parenthesis, or at the end of the URL when its
    // parentheses are balanced. Returns -1 when they are not. When they are not, the depths of the parentheses up to the end of
    // the URL are kept, for the autolinks that start in it.
    private int GetMarkdigUrlEnd(int start)
    {
        var scan = _markdigUrlScan;
        if (ReferenceEquals(scan.Text, _text) && scan.TextEnd == _end && start > scan.Start && start < scan.End)
        {
            // The depth before the start is the one of the scan at start - 1: the first position whose depth is lower closes no
            // parenthesis, else the parentheses are balanced when the depth at the end is the same
            var index = start - 1 - scan.Start;
            var closing = _markdigUrlLowerDepths[index];
            if (closing >= 0)
            {
                return scan.Start + closing;
            }

            return _markdigUrlDepths[^1] == _markdigUrlDepths[index] ? scan.End : -1;
        }

        var position = start;
        var depth = 0;
        while (true)
        {
            var c = position < _end ? _text[position] : '\0';
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return position;
                }
            }
            else if (IsMarkdigUrlEnd(position, c))
            {
                break;
            }

            position++;
        }

        if (depth == 0)
        {
            return position;
        }

        // The depth after each character, and the first character after it whose depth is lower
        _markdigUrlDepths.Clear();
        depth = 0;
        for (var i = start; i < position; i++)
        {
            depth += _text[i] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };
            _markdigUrlDepths.Add(depth);
        }

        _markdigUrlLowerDepths.Clear();
        CollectionsMarshal.SetCount(_markdigUrlLowerDepths, _markdigUrlDepths.Count);
        var lowerDepths = CollectionsMarshal.AsSpan(_markdigUrlLowerDepths);
        var depths = CollectionsMarshal.AsSpan(_markdigUrlDepths);
        var pending = new Stack<int>();
        for (var i = 0; i < depths.Length; i++)
        {
            while (pending.Count > 0 && depths[pending.Peek()] > depths[i])
            {
                lowerDepths[pending.Pop()] = i;
            }

            pending.Push(i);
        }

        foreach (var i in pending)
        {
            lowerDepths[i] = -1;
        }

        _markdigUrlScan = new MarkdigUrlScan(_text, _end, start, position);
        return -1;
    }

    private bool IsMarkdigUrlEnd(int position, char c)
    {
        if (IsMarkdigEndOfUri(c))
        {
            return true;
        }

        if (c == '&' && Scanners.EntityRegex().Match(_text, position, _end - position) is { Success: true, Index: var index } && index == position)
        {
            return true;
        }

        return c is '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~' && IsMarkdigEndOfUri(position + 1 < _end ? _text[position + 1] : '\0');

        static bool IsMarkdigEndOfUri(char c) => c is '\0' or ' ' or '\t' or '<' || char.IsControl(c);
    }

    // A domain of segments of letters, digits, hyphens and underscores separated by periods, with at least one period, and no
    // underscore in its last two segments. Markdig accepts the characters that are not ASCII and not punctuation or spaces.
    private static bool IsValidMarkdigDomain(ReadOnlySpan<char> link, int start, bool allowDomainWithoutPeriod)
    {
        var segmentCount = 1;
        var segmentHasCharacters = false;
        var lastUnderscoreSegment = -1;
        for (var i = start; i < link.Length; i++)
        {
            var c = link[i];
            if (!char.IsAsciiLetterOrDigit(c))
            {
                if (c == '.')
                {
                    if (!segmentHasCharacters)
                    {
                        return false;
                    }

                    segmentCount++;
                    segmentHasCharacters = false;
                    continue;
                }

                if (c is '/' or '?' or '#' or ':')
                {
                    break;
                }

                if (c == '_')
                {
                    lastUnderscoreSegment = segmentCount;
                }
                else if (c != '-' && IsSpaceOrPunctuation(c))
                {
                    return false;
                }
            }

            segmentHasCharacters = true;
        }

        return (segmentCount != 1 || allowDomainWithoutPeriod) && segmentHasCharacters && segmentCount - lastUnderscoreSegment >= 2;

        static bool IsSpaceOrPunctuation(char c) => c <= 127
            ? c is '\0' or '\t' or '\n' or '\f' or '\r' or ' ' || Scanners.IsEscapable(c)
            : CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.SpaceSeparator;
    }

    // A reference to a Jira issue starts after whitespace or (
    private bool IsJiraLinkStart(int position) => position <= _inlineStart || _text[position - 1] == '(' || Scanners.IsUnicodeWhitespace(GetRuneBefore(position));

    // A reference to a Jira issue: uppercase letters and digits that start with a letter, a hyphen and digits, before whitespace
    // or )
    private bool ParseJiraLink()
    {
        var position = _pos;
        while (position < _end && (char.IsAsciiLetterUpper(_text[position]) || char.IsAsciiDigit(_text[position])))
        {
            position++;
        }

        if (CharAt(position) != '-' || !char.IsAsciiDigit(CharAt(position + 1)))
        {
            return false;
        }

        position++;
        while (position < _end && char.IsAsciiDigit(_text[position]))
        {
            position++;
        }

        if (position < _end && _text[position] != ')' && !Scanners.IsUnicodeWhitespace(GetRuneAt(position)))
        {
            return false;
        }

        var link = NewNode(SyntaxKind.JiraLink, _pos, position);
        link.Append(NewNode(SyntaxKind.JiraLinkToken, _pos, position));
        _root.Append(link);
        _pos = position;
        return true;
    }

    // The characters where a construct of an extension can start: a tilde, the colon of a URL, the start of www.
    private bool IsExtensionCharacter(char c) => c switch
    {
        _ when _syntaxExtensions is not null && _syntaxExtensions.IsTrigger(c) => true,
        _ when _emoji is not null && _emoji.IsFirstCharacter(c) && !char.IsLetterOrDigit(_pos > _inlineStart ? _text[_pos - 1] : '\0') => true,
        '~' => _strikethrough || _subscript,
        '^' => _superscript,
        '+' => _inserted,
        '=' => _marked,
        '$' => _mathematics,
        '{' => _genericAttributes,
        ':' => _autolinks || _customContainers,
        >= 'A' and <= 'Z' when _jiraLinks => IsJiraLinkStart(_pos),
        'h' or 'f' or 'm' or 't' or 'w' when _markdigAutolinks is not null => IsMarkdigAutolinkStart(_pos),
        '\'' or '"' or '>' or '.' when _smartyPants => true,
        '"' when _citations => true,
        'w' => _autolinks && _pos + 3 < _end && _text[_pos + 1] == 'w' && _text[_pos + 2] == 'w' && _text[_pos + 3] == '.',
        _ => false,
    };

    private void ParseNewline()
    {
        var newline = _pos;
        _pos++;
        if (_scanningPipeTable && _firstNestedLineBreak < 0 && HasOpenContainer)
        {
            _firstNestedLineBreak = newline;
        }

        // The spaces at the end of the line are not text: two or more of them make a hard line break
        var last = _root.LastChild;
        var spacesStart = newline;
        if (last is { Kind: SyntaxKind.TextToken })
        {
            while (spacesStart > last.Start && _text[spacesStart - 1] == ' ')
            {
                spacesStart--;
            }

            last.End = spacesStart;
        }

        if (newline - spacesStart >= 2)
        {
            _root.Append(NewNode(SyntaxKind.HardLineBreakToken, spacesStart, newline + 1));
        }
        else
        {
            _root.Append(NewNode(SyntaxKind.LineEndingToken, newline, newline + 1));
        }

        // The spaces at the start of the next line are not text either
        while (_pos < _end && _text[_pos] == ' ')
        {
            _pos++;
        }
    }

    private void ParseBackslash()
    {
        var start = _pos;
        _pos++;
        if (PeekIs('\n'))
        {
            _pos++;
            _root.Append(NewNode(SyntaxKind.HardLineBreakToken, start, _pos));
            while (_pos < _end && _text[_pos] == ' ')
            {
                _pos++;
            }
        }
        else if (_pos < _end && Scanners.IsEscapable(_text[_pos]))
        {
            _pos++;
            _root.Append(NewNode(SyntaxKind.EscapedCharacterToken, start, _pos));
        }
        else
        {
            AppendText(start, _pos);
        }
    }

    private void ParseBackticks()
    {
        var start = _pos;
        while (_pos < _end && _text[_pos] == '`')
        {
            _pos++;
        }

        var tickCount = _pos - start;
        var afterOpenTicks = _pos;
        var position = _pos;

        // A search reached the end of the range: the last run of each length after it is known. The content of an extension has
        // its own end, which can split a run, so it has its own state (see ParseExtensionContent).
        var hasCloser = _end != _backtickRunsScannedEnd || (_backtickRuns.TryGetValue(tickCount, out var lastRun) && lastRun >= afterOpenTicks);
        while (hasCloser)
        {
            if (position >= _end)
            {
                _backtickRunsScannedEnd = _end;
                break;
            }

            if (_text[position] != '`')
            {
                // As in Markdig, a code span does not continue on a line that starts with a pipe after a column delimiter of the
                // paragraph, not in a delimiter of emphasis or a bracket: it is probably the start of a code span in a cell
                if (_text[position] == '\n' && HasPipeForMarkdig() && CharAt(position + 1) == '|')
                {
                    break;
                }

                position++;
                continue;
            }

            var runStart = position;
            while (position < _end && _text[position] == '`')
            {
                position++;
            }

            ref var lastStart = ref CollectionsMarshal.GetValueRefOrAddDefault(_backtickRuns, position - runStart, out _);
            lastStart = Math.Max(lastStart, runStart);
            if (position - runStart == tickCount)
            {
                var code = NewNode(SyntaxKind.CodeSpan, start, position);
                code.Append(NewNode(SyntaxKind.BacktickStringToken, start, afterOpenTicks));
                AppendLines(code, SyntaxKind.CodeTextToken, afterOpenTicks, runStart);
                code.Append(NewNode(SyntaxKind.BacktickStringToken, runStart, position));
                _root.Append(code);
                _pos = position;
                return;
            }
        }

        // No closing backticks: the opening ones are text
        AppendText(start, afterOpenTicks);
    }

    private bool ParseAutolink()
    {
        var length = Match(Scanners.EmailAutolinkRegex());
        if (length == 0)
        {
            length = Match(Scanners.AutolinkRegex());
        }

        if (length == 0)
        {
            return false;
        }

        var link = NewNode(SyntaxKind.Autolink, _pos, _pos + length);
        link.Append(NewNode(SyntaxKind.AutolinkToken, _pos, _pos + length));
        _root.Append(link);
        _pos += length;
        return true;
    }

    private bool ParseHtmlTag()
    {
        var length = CharAt(_pos + 1) is '!' or '?' ? GetHtmlTagLengthWithTerminator() : Match(Scanners.HtmlTagRegex());
        if (length == 0)
        {
            return false;
        }

        var html = NewNode(SyntaxKind.RawHtml, _pos, _pos + length);
        AppendLines(html, SyntaxKind.HtmlTagToken, _pos, _pos + length);
        _root.Append(html);
        _pos += length;
        return true;
    }

    // A comment, a processing instruction, a CDATA section or a declaration ends at the first terminator after its start: the
    // regex would look for it up to the end of the subject at each opener. The position of the next terminator of each kind is
    // remembered instead, as cmark does, so that the openers that have none are text at once.
    private int GetHtmlTagLengthWithTerminator()
    {
        var text = _text.AsSpan(_pos, _end - _pos);
        var (kind, terminator, searchStart) = text switch
        {
            ['<', '?', ..] => (0, "?>", 2),
            ['<', '!', '-', '-', ..] => (1, "-->", 2),
            ['<', '!', '[', 'C', 'D', 'A', 'T', 'A', '[', ..] => (2, "]]>", 9),
            ['<', '!', var c, ..] when char.IsAsciiLetter(c) => (3, ">", 3),
            _ => (-1, "", 0),
        };

        if (kind < 0)
        {
            return 0;
        }

        // <!--> and <!---> are comments: the terminator starts in the opener
        var terminatorStart = FindHtmlTerminator(kind, terminator, _pos + searchStart);
        return terminatorStart < 0 ? 0 : terminatorStart + terminator.Length - _pos;
    }

    // Where the first terminator at or after the position starts, before the end of the subject, or -1
    private int FindHtmlTerminator(int kind, string terminator, int from)
    {
        ref var cache = ref _htmlTerminators[kind];
        var isValid = ReferenceEquals(cache.Text, _text) && from >= cache.From && (cache.Position < 0 ? _end <= cache.End : cache.Position >= from);
        if (!isValid)
        {
            var index = _text.AsSpan(from, _end - from).IndexOf(terminator, StringComparison.Ordinal);
            cache = new HtmlTerminator(_text, from, _end, index < 0 ? -1 : from + index);
        }

        return cache.Position >= 0 && cache.Position + terminator.Length <= _end ? cache.Position : -1;
    }

    private bool ParseEntity()
    {
        var length = Match(Scanners.EntityRegex());
        if (length == 0)
        {
            return false;
        }

        _root.Append(NewNode(SyntaxKind.EntityToken, _pos, _pos + length));
        _pos += length;
        return true;
    }

    private int Match(System.Text.RegularExpressions.Regex regex)
    {
        // The span API does not allocate a Match. The patterns are anchored by ^, which is the start of the span, as it is the
        // start of the range of Match(string, int, int).
        foreach (var match in regex.EnumerateMatches(_text.AsSpan(_pos, _end - _pos)))
        {
            return match.Length;
        }

        return 0;
    }

    // The rules of markdown-cjk-friendly, as Markdig applies them: a delimiter next to a CJK character can open or close even
    // after or before punctuation. withinWord: whether the delimiter can open or close in a word, which _ cannot.
    private void GetCjkFriendlyFlanking(int start, Rune before, Rune after, bool withinWord, out bool canOpen, out bool canClose)
    {
        var beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
        var afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
        if (beforeIsWhitespace || afterIsWhitespace)
        {
            canOpen = !afterIsWhitespace;
            canClose = !beforeIsWhitespace;
            return;
        }

        // A variation selector belongs to the character before it
        var main = before;
        var isVariation = before.Value is >= 0xFE00 and <= 0xFE0E;
        if (isVariation)
        {
            var mainEnd = start - before.Utf16SequenceLength;
            main = mainEnd > _textStart ? GetRuneBefore(mainEnd) : new Rune('\n');
        }

        var beforeIsPunctuation = Scanners.IsUnicodePunctuation(main);
        var afterIsPunctuation = Scanners.IsUnicodePunctuation(after);
        canOpen = beforeIsPunctuation;
        canClose = afterIsPunctuation;
        if (!withinWord)
        {
            return;
        }

        var beforeIsCjk = IsCjk(main) || (isVariation ? before.Value == 0xFE01 && main.Value is 0x2018 or 0x2019 or 0x201C or 0x201D : main.Value is >= 0xE0100 and <= 0xE01EF);
        var eitherIsCjk = beforeIsCjk || IsCjk(after);
        canOpen |= eitherIsCjk || !afterIsPunctuation;
        canClose |= eitherIsCjk || !beforeIsPunctuation;
    }

    // The CJK characters of markdown-cjk-friendly, as of Unicode 17
    private static bool IsCjk(Rune rune) => rune.Value is >= 0x1100 and (
        <= 0x11ff
        or 0x20a9
        or >= 0x2329 and <= 0x232a
        or >= 0x2630 and <= 0x2637
        or >= 0x268a and <= 0x268f
        or >= 0x2e80 and <= 0x2e99
        or >= 0x2e9b and <= 0x2ef3
        or >= 0x2f00 and <= 0x2fd5
        or >= 0x2ff0 and <= 0x303e
        or >= 0x3041 and <= 0x3096
        or >= 0x3099 and <= 0x30ff
        or >= 0x3105 and <= 0x312f
        or >= 0x3131 and <= 0x318e
        or >= 0x3190 and <= 0x31e5
        or >= 0x31ef and <= 0x321e
        or >= 0x3220 and <= 0x3247
        or >= 0x3250 and <= 0xa48c
        or >= 0xa490 and <= 0xa4c6
        or >= 0xa960 and <= 0xa97c
        or >= 0xac00 and <= 0xd7a3
        or >= 0xd7b0 and <= 0xd7c6
        or >= 0xd7cb and <= 0xd7fb
        or >= 0xf900 and <= 0xfaff
        or >= 0xfe10 and <= 0xfe19
        or >= 0xfe30 and <= 0xfe52
        or >= 0xfe54 and <= 0xfe66
        or >= 0xfe68 and <= 0xfe6b
        or >= 0xff01 and <= 0xffbe
        or >= 0xffc2 and <= 0xffc7
        or >= 0xffca and <= 0xffcf
        or >= 0xffd2 and <= 0xffd7
        or >= 0xffda and <= 0xffdc
        or >= 0xffe0 and <= 0xffe6
        or >= 0xffe8 and <= 0xffee
        or >= 0x16fe0 and <= 0x16fe4
        or >= 0x16ff0 and <= 0x16ff6
        or >= 0x17000 and <= 0x18cd5
        or >= 0x18cff and <= 0x18d1e
        or >= 0x18d80 and <= 0x18df2
        or >= 0x1aff0 and <= 0x1aff3
        or >= 0x1aff5 and <= 0x1affb
        or >= 0x1affd and <= 0x1affe
        or >= 0x1b000 and <= 0x1b122
        or 0x1b132
        or >= 0x1b150 and <= 0x1b152
        or 0x1b155
        or >= 0x1b164 and <= 0x1b167
        or >= 0x1b170 and <= 0x1b2fb
        or >= 0x1d300 and <= 0x1d356
        or >= 0x1d360 and <= 0x1d376
        or 0x1f200
        or 0x1f202
        or >= 0x1f210 and <= 0x1f219
        or >= 0x1f21b and <= 0x1f22e
        or >= 0x1f230 and <= 0x1f231
        or 0x1f237
        or 0x1f23b
        or >= 0x1f240 and <= 0x1f248
        or >= 0x1f260 and <= 0x1f265
        or >= 0x20000 and <= 0x3fffd);

    private void HandleDelimiter(char c)
    {
        var start = _pos;
        while (_pos < _end && _text[_pos] == c)
        {
            _pos++;
        }

        var count = _pos - start;

        // A run of more than two tildes is text in GFM. A run shorter than its emphasis needs is text: ++ and == need two.
        var extra = IsExtraEmphasis(c);
        if ((c == '~' && !_subscript && count > 2) || (extra && count < GetMinimumCount(c)))
        {
            AppendText(start, _pos);
            return;
        }

        // The extra emphasis read an entity as the character it stands for, and some punctuation as text: 10^-18^
        var before = start == _textStart ? new Rune('\n') : extra && _root.LastChild is { Kind: SyntaxKind.EntityToken } entity && entity.End == start ? GetLastRune(HtmlEntities.Decode(_text[entity.Start..entity.End])) : GetRuneBefore(start);
        var after = _pos >= _end ? (_pos < _lookaheadEnd ? GetRuneAt(_pos) : new Rune('\n')) : extra && Match(Scanners.EntityRegex()) is > 0 and var entityLength ? GetFirstRune(HtmlEntities.Decode(_text.Substring(_pos, entityLength))) : GetRuneAt(_pos);
        var afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
        var afterIsPunctuation = Scanners.IsUnicodePunctuation(after) && !(extra && IsPunctuationException(after));
        var beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
        var beforeIsPunctuation = Scanners.IsUnicodePunctuation(before);
        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!(beforeIsPunctuation && !(extra && IsPunctuationException(before))) || afterIsWhitespace || Scanners.IsUnicodePunctuation(after));
        bool canOpen;
        bool canClose;
        if (_cjkFriendly)
        {
            GetCjkFriendlyFlanking(start, before, after, c is not ('_' or '"'), out canOpen, out canClose);
        }
        // As in Markdig, the quotes of a citation cannot open or close in a word, as _, whatever the punctuation around them
        else if (c is '_' or '"')
        {
            canOpen = leftFlanking && (!rightFlanking || beforeIsPunctuation);
            canClose = rightFlanking && (!leftFlanking || Scanners.IsUnicodePunctuation(after));
        }
        else
        {
            canOpen = leftFlanking;
            canClose = rightFlanking;
        }

        // Markdig reads the quotes of a citation that cannot open or close as SmartyPants, one by one
        if (c == '"' && _smartyPants && !canOpen && !canClose)
        {
            _pos = start;
            if (!ParseSmartyPant())
            {
                AppendText(_pos, _pos + 1);
                _pos++;
            }

            return;
        }

        var node = AppendText(start, _pos);
        if (canOpen || canClose)
        {
            node.IsDelimiterRun = true;
            var delimiter = new Delimiter(c, count, node, canOpen, canClose) { Previous = _delimiters };
            if (_delimiters is not null)
            {
                _delimiters.Next = delimiter;
            }

            _delimiters = delimiter;
            if (c < _pendingDelimiters.Length)
            {
                _pendingDelimiters[c]++;
            }
        }
    }

    /// <summary>Gets the normalized labels of the footnotes of the document, which the block parser adds as it reads their definitions.</summary>
    public HashSet<string> Footnotes { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the normalized labels of the headings, which a reference link without definition links to with auto-identifiers.</summary>
    public HashSet<string> HeadingLabels { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the labels of the abbreviations of the document, with the length of their title, which the block parser adds as it reads their definitions.</summary>
    public Dictionary<string, int> Abbreviations { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets how many characters the abbreviations can add to the document, so that a short text cannot render as a huge one.</summary>
    public long MaximumAbbreviationExpansion { get; set; }

    // The abbreviations of the text, as Markdig finds them once the inlines are parsed, in each of its literals: a run of text
    // tokens, which an escaped character can start. The longest label is found at the start of a literal or after whitespace,
    // and must be followed by whitespace, or by ASCII punctuation up to whitespace or the end of the literal.
    private void MarkAbbreviations(InlineNode container)
    {
        var run = new List<InlineNode>();
        var child = container.FirstChild;
        while (child is not null)
        {
            if (IsLiteral(child))
            {
                run.Clear();
                run.Add(child);
                while (run[^1].Next is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } next && next.Start == run[^1].End)
                {
                    run.Add(next);
                }

                child = run[^1].Next;
                if (!MarkAbbreviationsInLiteral(run))
                {
                    return;
                }

                continue;
            }

            if (!child.IsToken && child.Kind is not (SyntaxKind.CodeSpan or SyntaxKind.Autolink or SyntaxKind.ExtendedAutolink or SyntaxKind.RawHtml or SyntaxKind.MathInline or SyntaxKind.Abbreviation or SyntaxKind.FootnoteReference))
            {
                MarkAbbreviations(child);
            }

            child = child.Next;
        }
    }

    private static bool IsLiteral(InlineNode node) => node.Kind == SyntaxKind.EscapedCharacterToken || (node.Kind == SyntaxKind.TextToken && !node.IsDelimiterRun);

    private bool MarkAbbreviationsInLiteral(List<InlineNode> run)
    {
        // The content of the literal, where an escaped character is the character, and where each character is in the subject
        var content = new System.Text.StringBuilder();
        var positions = new List<int>();
        foreach (var token in run)
        {
            for (var position = token.Kind == SyntaxKind.EscapedCharacterToken ? token.End - 1 : token.Start; position < token.End; position++)
            {
                content.Append(_text[position]);
                positions.Add(position);
            }
        }

        var text = content.ToString();
        var after = run[^1].End < _textEnd ? _text[run[^1].End] : '\0';
        var ranges = new List<(int Start, int End)>();
        var start = 0;

        // The longest label at each position
        if (_abbreviationMatcher is null || _abbreviationMatcherLabelCount != Abbreviations.Count)
        {
            _abbreviationMatcher = new AbbreviationMatcher(Abbreviations.Keys);
            _abbreviationMatcherLabelCount = Abbreviations.Count;
        }

        if (_abbreviationLabels.Length < text.Length)
        {
            _abbreviationLabels = new string?[Math.Max(text.Length, _abbreviationLabels.Length * 2)];
        }

        var labels = _abbreviationLabels;
        _abbreviationMatcher.FindLongestLabels(text, labels.AsSpan(0, text.Length));
        for (var i = 0; i < text.Length; i++)
        {
            // An abbreviation starts at the start of the literal or after whitespace
            if (i != start)
            {
                var whitespace = i - 1;
                while (whitespace < text.Length && !IsAbbreviationWhitespace(text[whitespace]))
                {
                    whitespace++;
                }

                if (whitespace >= text.Length - 1)
                {
                    break;
                }

                i = whitespace + 1;
            }

            var label = labels[i];
            if (label is null || !IsValidAbbreviationEnding(text, i + label.Length, after))
            {
                continue;
            }

            var length = label.Length;
            var titleLength = Abbreviations[label];

            // Each abbreviation writes its title
            if (_abbreviationExpansion + titleLength > MaximumAbbreviationExpansion)
            {
                if (ranges.Count > 0)
                {
                    WrapRanges(run, ranges, SyntaxKind.Abbreviation);
                }

                return false;
            }

            _abbreviationExpansion += titleLength;

            // The escaped character at the start of the literal starts at its backslash
            var rangeStart = i == 0 && run[0].Kind == SyntaxKind.EscapedCharacterToken ? run[0].Start : positions[i];
            ranges.Add((rangeStart, positions[i + length - 1] + 1));
            start = i + length;
            i = start - 1;
        }

        if (ranges.Count > 0)
        {
            WrapRanges(run, ranges, SyntaxKind.Abbreviation);
        }

        return true;
    }

    // After the abbreviation, ASCII punctuation up to whitespace, the end of the text, or the character after the literal
    private static bool IsValidAbbreviationEnding(string text, int index, char after)
    {
        for (; index <= text.Length; index++)
        {
            var c = index < text.Length ? text[index] : after;
            if (IsAbbreviationWhitespace(c))
            {
                break;
            }

            if (c != '\0' && !(c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAbbreviationWhitespace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    // Inline math between $ or $$, as Markdig reads it: the opening is after whitespace or punctuation, the closing has at
    // least as many $ and is before whitespace or punctuation, and spaces after the opening need spaces before the closing.
    // A $ after a backslash is text of the math. The math does not span lines.
    // A construct of an inline extension at its trigger character: the first extension of the character that reads one
    private bool ParseInlineExtension(MarkdownInlineExtension[] extensions)
    {
        var previous = _pos > _inlineStart ? _text[_pos - 1] : '\0';
        foreach (var extension in extensions)
        {
            _extensionRanges.Clear();
            var index = _syntaxExtensions!.GetIndex(extension);
            var context = new MarkdownInlineParseContext(_text.AsSpan(_pos, _end - _pos), previous, _options, _extensionRanges, States, index);
            if (!extension.TryParse(ref context))
            {
                continue;
            }

            if (context.Length == 0)
            {
                throw new InvalidOperationException($"The inline extension '{extension.Name}' read a construct without calling {nameof(MarkdownInlineParseContext.Complete)}.");
            }

            // The line endings of a paragraph that is scanned for a pipe table end its rows: a construct does not span them, as a
            // code span does not continue on the next row. Its pipes are its own, as the ones of a code span.
            if (_scanningPipeTable && _text.AsSpan(_pos, context.Length).Contains('\n'))
            {
                continue;
            }

            // The content of the construct can have constructs of extensions, which report their own parts
            AppendExtensionInline(index, context.Length, [.. _extensionRanges]);
            return true;
        }

        return false;
    }

    // The node of a construct of an inline extension: its parts, and text for the characters between them
    private void AppendExtensionInline(int index, int length, ExtensionRange[] ranges)
    {
        var start = _pos;
        var end = start + length;
        var node = NewNode(SyntaxKind.ExtensionInline, start, end);
        _root.Append(node);
        var position = start;
        var first = true;
        foreach (var range in ranges)
        {
            var rangeStart = start + range.Start;
            var rangeEnd = rangeStart + range.Length;
            if (rangeStart > position)
            {
                AppendExtensionTokens(node, SyntaxKind.ExtensionTextToken, position, rangeStart, index, ref first);
            }

            if (range.Kind == ExtensionRangeKind.Inlines && _extensionDepth < MaximumExtensionDepth)
            {
                ParseExtensionContent(node, rangeStart, rangeEnd);
            }
            else
            {
                AppendExtensionTokens(node, range.Kind == ExtensionRangeKind.Marker ? SyntaxKind.ExtensionMarkerToken : SyntaxKind.ExtensionTextToken, rangeStart, rangeEnd, index, ref first);
            }

            position = rangeEnd;
        }

        if (position < end)
        {
            AppendExtensionTokens(node, SyntaxKind.ExtensionTextToken, position, end, index, ref first);
        }

        _pos = end;
    }

    // The tokens of a part of a construct, split at the line endings. The first token of the construct has the index of the
    // extension: it starts at the trigger character, which is not a line ending.
    private void AppendExtensionTokens(InlineNode parent, SyntaxKind kind, int start, int end, int index, ref bool first)
    {
        var lineStart = start;
        for (var i = start; i <= end; i++)
        {
            if (i < end && _text[i] != '\n')
            {
                continue;
            }

            if (i > lineStart)
            {
                var token = NewNode(kind, lineStart, i);
                if (first)
                {
                    token.Value = index + 1;
                    first = false;
                }

                parent.Append(token);
            }

            if (i < end)
            {
                parent.Append(NewNode(SyntaxKind.LineEndingToken, i, i + 1));
                lineStart = i + 1;
            }
        }
    }

    // The content of a construct, read as inlines on their own: the delimiters and the brackets around the construct do not
    // match the ones in it, and its pipes are text in a pipe table
    private void ParseExtensionContent(InlineNode parent, int start, int end)
    {
        var root = _root;
        var previousEnd = _end;
        var brackets = _brackets;
        var activeBrackets = _activeBrackets;
        var backtickRunsScannedEnd = _backtickRunsScannedEnd;
        var anchorScanNode = _anchorScanNode;
        var anchorScanIsOpen = _anchorScanIsOpen;
        var stackBottom = _delimiters;
        var scanningPipeTable = _scanningPipeTable;
        var pipeTableCell = _pipeTableCell;
        _root = parent;
        _pos = start;
        _end = end;
        _brackets = null;
        _activeBrackets = 0;

        // The runs that the outer searches recorded are kept: they only make a search run, which reads the runs as the content
        // ends them
        _backtickRunsScannedEnd = -1;
        _anchorScanNode = null;
        _anchorScanIsOpen = false;
        _scanningPipeTable = false;
        _pipeTableCell = false;
        _extensionDepth++;
        try
        {
            while (_pos < _end)
            {
                ParseInline();
            }

            ProcessEmphasis(stackBottom);
        }
        finally
        {
            _extensionDepth--;
            _root = root;
            _end = previousEnd;
            _brackets = brackets;
            _activeBrackets = activeBrackets;
            _backtickRunsScannedEnd = backtickRunsScannedEnd;
            _anchorScanNode = anchorScanNode;
            _anchorScanIsOpen = anchorScanIsOpen;
            _scanningPipeTable = scanningPipeTable;
            _pipeTableCell = pipeTableCell;
        }
    }

    private bool ParseMath()
    {
        var open = _pos;
        var previous = open == _textStart ? '\0' : _text[open - 1];
        if (previous == '$' || !(IsMathWhitespace(previous) || IsMathPunctuation(previous)))
        {
            return false;
        }

        var i = open + 1;
        var openDollars = 1;
        if (MathAt(i) == '$')
        {
            openDollars++;
            i++;
        }

        var c = MathAt(i);
        var openNextIsWhitespace = IsMathWhitespace(c);
        while (c is ' ' or '\t')
        {
            c = MathAt(++i);
        }

        var contentStart = i;
        var lastWhitespace = -1;
        var closeDollars = 0;
        var pc = '$';
        while (c != '\0')
        {
            if (c is '\r' or '\n')
            {
                return false;
            }

            if (pc != '\\')
            {
                if (c is ' ' or '\t')
                {
                    if (lastWhitespace < 0)
                    {
                        lastWhitespace = i;
                    }
                }
                else
                {
                    var closing = c == '$';
                    if (closing)
                    {
                        while (MathAt(i) == '$')
                        {
                            closeDollars++;
                            i++;
                        }

                        c = MathAt(i);
                    }

                    if (closeDollars >= openDollars)
                    {
                        break;
                    }

                    lastWhitespace = -1;
                    if (closing)
                    {
                        pc = '$';
                        continue;
                    }
                }
            }

            if (closeDollars > 0)
            {
                closeDollars = 0;
            }
            else
            {
                pc = c;
                c = MathAt(++i);
            }
        }

        if (closeDollars < openDollars)
        {
            return false;
        }

        var closePreviousIsWhitespace = IsMathWhitespace(pc);
        if ((!IsMathPunctuation(c) && !IsMathWhitespace(c)) || openNextIsWhitespace != closePreviousIsWhitespace)
        {
            return false;
        }

        // The content is the text between the dollars, without the whitespace around it; the $ that close beyond the
        // number that opened are text of the math when no whitespace is before them
        int contentEnd;
        int closeStart;
        if (closePreviousIsWhitespace && lastWhitespace > 0)
        {
            contentEnd = lastWhitespace;
            closeStart = i - closeDollars;
        }
        else
        {
            contentEnd = i - openDollars;
            closeStart = contentEnd;
        }

        var math = NewNode(SyntaxKind.MathInline, open, i);
        math.Append(NewNode(SyntaxKind.MathDelimiterToken, open, open + openDollars));
        if (contentEnd > contentStart)
        {
            math.Append(NewNode(SyntaxKind.MathTextToken, contentStart, contentEnd));
        }

        math.Append(NewNode(SyntaxKind.MathDelimiterToken, closeStart, i));
        _root.Append(math);
        _pos = i;
        return true;
    }

    private char MathAt(int index) => index < _end ? _text[index] : '\0';

    // Attributes after a delimiter that is text, or after a bracket that opens no link, are for that delimiter in Markdig, which
    // writes them nowhere
    private static void DetachAttributes(InlineNode container)
    {
        InlineNode? previous = null;
        for (var child = container.FirstChild; child is not null; child = child.Next)
        {
            if (child.Kind == SyntaxKind.GenericAttributes)
            {
                if (previous is { IsDelimiterRun: true } or { Kind: SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken } && child.FirstChild is { Value: 0 } first)
                {
                    first.Value = 1;
                }

                continue;
            }

            if (!child.IsToken)
            {
                DetachAttributes(child);
            }

            previous = child;
        }
    }

    // Generic attributes: {#id .class name=value}, with the line ending after them. They apply to the inline before them, or to
    // the block when the inline before them is text; the whitespace before attributes that end the text is not text.
    private bool ParseAttributes()
    {
        if (_attributesOutcomes is null)
        {
            _attributesOutcomesStart = _pos;
            _attributesOutcomes = AttributesScanner.ComputeOutcomes(_text, _pos, _end);
        }

        if (!AttributesScanner.IsValid(_attributesOutcomes, _attributesOutcomesStart, _pos) || AttributesScanner.TryParse(_text, _pos, _end, out _, _textStart) is not (>= 0 and var end))
        {
            return false;
        }

        // Markdig skips the line ending after them, so that attributes on their own line are for the text after them, unless the
        // paragraph has a column delimiter that is not in a delimiter of emphasis or a bracket: it ends a row
        if (HasPipeForMarkdig() && _text[end - 1] == '\n')
        {
            end--;
        }

        // The text before attributes that end the text, and before the attributes before them, ends with no whitespace
        var afterText = false;
        if (_text.AsSpan(end, _end - end).IndexOfAnyExcept(" \t\n\v\f\r") < 0)
        {
            while (GetLastNonAttributes(_root) is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } text)
            {
                afterText = true;
                while (text.End > text.Start && _text[text.End - 1] is ' ' or '\t' or '\v' or '\f')
                {
                    text.End--;
                }

                if (text.End > text.Start)
                {
                    break;
                }

                // The attributes after the whitespace were after text: they are for the block
                for (var next = text.Next; next is not null; next = next.Next)
                {
                    if (next.FirstChild is { Value: 0 } token)
                    {
                        token.Value = 2;
                    }
                }

                text.Unlink();
            }
        }

        var attributes = NewNode(SyntaxKind.GenericAttributes, _pos, end);
        AppendLines(attributes, SyntaxKind.AttributesToken, _pos, end);
        if (afterText && attributes.FirstChild is { } first && (GetLastNonAttributes(_root) is not { Kind: SyntaxKind.TextToken } last || last.End < _pos))
        {
            // The whitespace before them was text: they are for the block
            first.Value = 2;
        }

        // Markdig gives the attributes after a column delimiter of a table to the delimiter, which is then text, and the ones at the
        // start of a row to its line break: they are for no element
        if (attributes.FirstChild is { } attributesToken && (_scanningPipeTable ? _root.LastChild is { Kind: SyntaxKind.PipeToken } pipe && pipe.End == _pos : _pipeTableCell && _pos == _cellStart && _pos > _textStart && _text[_pos - 1] is '|' or '\n'))
        {
            attributesToken.Value = 1;
        }

        _root.Append(attributes);
        _pos = end;
        return true;
    }

    private char CharAt(int position) => position < _end ? _text[position] : '\0';

    // An emoji, as Markdig reads it: the longest shortcode or smiley, which is not after a letter or a digit. When its last
    // characters are a delimiter of emphasis, they are left to the emphasis, and the emoji is read after it.
    private bool ParseEmoji()
    {
        if (char.IsLetterOrDigit(_pos > _inlineStart ? _text[_pos - 1] : '\0') || _emoji!.MatchLongest(_text.AsSpan(_pos, _end - _pos)) is not { } emoji)
        {
            return false;
        }

        var last = emoji[^1];
        if (IsEmphasisCharacter(last))
        {
            var prefixLength = emoji.Length - 1;
            while (prefixLength > 0 && emoji[prefixLength - 1] == last)
            {
                prefixLength--;
            }

            if (prefixLength > 0)
            {
                var pending = AppendText(_pos, _pos + prefixLength);
                pending.IsDelimiterRun = true;
                _pendingEmojis.Add((pending, emoji.Length));
                _pos += prefixLength;
                return true;
            }
        }

        _root.Append(NewNode(SyntaxKind.EmojiToken, _pos, _pos + emoji.Length));
        _pos += emoji.Length;
        return true;
    }

    private bool IsEmphasisCharacter(char c) => c switch
    {
        _ when _syntaxExtensions is not null && _syntaxExtensions.GetDelimiterIndex(c) >= 0 => true,
        '*' or '_' => true,
        '~' => _strikethrough || _subscript,
        '^' => _superscript,
        '+' => _inserted,
        '=' => _marked,
        ':' => _customContainers,
        '"' => _citations,
        _ => false,
    };

    // The emoji whose delimiters the emphasis left as text right after them
    private void ResolvePendingEmojis()
    {
        foreach (var (node, length) in _pendingEmojis)
        {
            var end = node.Start + length;
            if (node.Next is { Kind: SyntaxKind.TextToken } suffix && suffix.Start == node.End && suffix.End >= end)
            {
                node.ChangeKind(SyntaxKind.EmojiToken);
                node.IsDelimiterRun = false;
                node.End = end;
                if (suffix.End == end)
                {
                    suffix.Unlink();
                }
                else
                {
                    suffix.Start = end;
                }
            }
        }
    }

    // SmartyPants, as Markdig reads it: a quote that can open or close as _ can, a pair of angle quotes, an ellipsis that ends a word
    private bool ParseSmartyPant()
    {
        var start = _pos;
        var c = _text[start];
        MarkdownSmartyPant pant;
        int end;
        switch (c)
        {
            case '\'' or '"':
                var isDouble = c == '"' || CharAt(start + 1) == '\'';
                end = start + (c == '\'' && isDouble ? 2 : 1);
                pant = isDouble ? MarkdownSmartyPant.LeftDoubleQuote : MarkdownSmartyPant.LeftQuote;
                break;
            case '<' or '>' when CharAt(start + 1) == c:
                end = start + 2;
                pant = c == '<' ? MarkdownSmartyPant.LeftAngleQuote : MarkdownSmartyPant.RightAngleQuote;
                break;
            case '.' when CharAt(start + 1) == '.' && CharAt(start + 2) == '.':
                end = start + 3;
                pant = MarkdownSmartyPant.Ellipsis;
                break;
            default:
                return false;
        }

        GetSmartyPantFlanking(start, end, out var canOpen, out var canClose);
        switch (pant)
        {
            case MarkdownSmartyPant.LeftQuote or MarkdownSmartyPant.LeftDoubleQuote:
                if (canOpen == canClose)
                {
                    return false;
                }

                if (canClose)
                {
                    pant = pant == MarkdownSmartyPant.LeftQuote ? MarkdownSmartyPant.RightQuote : MarkdownSmartyPant.RightDoubleQuote;
                }

                break;
            case MarkdownSmartyPant.LeftAngleQuote when !canOpen || canClose:
            case MarkdownSmartyPant.RightAngleQuote or MarkdownSmartyPant.Ellipsis when canOpen || !canClose:
                return false;
        }

        var node = NewNode(SyntaxKind.SmartyPantToken, start, end);
        node.Value = (int)pant;
        _root.Append(node);
        if (pant != MarkdownSmartyPant.Ellipsis)
        {
            _smartQuotes.Add(node);
        }

        _pos = end;
        return true;
    }

    // The start and the end of the text are whitespace and punctuation for Markdig
    private void GetSmartyPantFlanking(int start, int end, out bool canOpen, out bool canClose)
    {
        var beforeIsWhitespace = true;
        var beforeIsPunctuation = true;
        var beforeIsException = false;
        if (start > _inlineStart)
        {
            var before = GetRuneBefore(start);
            beforeIsWhitespace = Scanners.IsUnicodeWhitespace(before);
            beforeIsPunctuation = !beforeIsWhitespace && Scanners.IsUnicodePunctuation(before);
            beforeIsException = beforeIsPunctuation && IsPunctuationException(before);
        }

        var afterIsWhitespace = true;
        var afterIsPunctuation = true;
        var afterIsException = false;
        if (end < _lookaheadEnd)
        {
            var after = GetRuneAt(end);
            afterIsWhitespace = Scanners.IsUnicodeWhitespace(after);
            afterIsPunctuation = !afterIsWhitespace && Scanners.IsUnicodePunctuation(after);
            afterIsException = afterIsPunctuation && IsPunctuationException(after);
        }

        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || afterIsException || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!beforeIsPunctuation || beforeIsException || afterIsWhitespace || afterIsPunctuation);
        canOpen = leftFlanking && (!rightFlanking || beforeIsPunctuation);
        canClose = rightFlanking && (!leftFlanking || afterIsPunctuation);
    }

    // A closing quote closes the last opening quote of its kind, and the quotes that are opened after it are text
    private void PairSmartQuotes()
    {
        var openers = new Stack<InlineNode>();
        foreach (var quote in _smartQuotes)
        {
            var pant = (MarkdownSmartyPant)quote.Value;
            if (pant is MarkdownSmartyPant.LeftQuote or MarkdownSmartyPant.LeftDoubleQuote or MarkdownSmartyPant.LeftAngleQuote)
            {
                openers.Push(quote);
                continue;
            }

            var opening = pant switch
            {
                MarkdownSmartyPant.RightQuote => MarkdownSmartyPant.LeftQuote,
                MarkdownSmartyPant.RightDoubleQuote => MarkdownSmartyPant.LeftDoubleQuote,
                _ => MarkdownSmartyPant.LeftAngleQuote,
            };
            var found = false;
            while (openers.TryPop(out var opener))
            {
                if ((MarkdownSmartyPant)opener.Value == opening)
                {
                    found = true;
                    break;
                }

                ToSmartyPantText(opener);
            }

            if (!found)
            {
                ToSmartyPantText(quote);
            }
        }

        foreach (var opener in openers)
        {
            ToSmartyPantText(opener);
        }
    }

    // A quote that Markdig writes as text, in a literal of its own
    private static void ToSmartyPantText(InlineNode quote)
    {
        quote.ChangeKind(SyntaxKind.TextToken);
        quote.Value = 0;
        quote.IsDelimiterRun = true;
    }

    // The dashes of the literals of Markdig: -- and ---, except after an escape that starts the literal
    private void MarkDashes(InlineNode container)
    {
        var child = container.FirstChild;
        while (child is not null)
        {
            if (IsLiteral(child))
            {
                var first = child;
                var last = child;
                while (last.Next is { Kind: SyntaxKind.TextToken, IsDelimiterRun: false } next && next.Start == last.End)
                {
                    last = next;
                }

                child = last.Next;
                MarkDashesInLiteral(first, last);
                continue;
            }

            if (!child.IsToken && child.Kind is not (SyntaxKind.CodeSpan or SyntaxKind.Autolink or SyntaxKind.ExtendedAutolink or SyntaxKind.RawHtml or SyntaxKind.MathInline or SyntaxKind.FootnoteReference))
            {
                MarkDashes(child);
            }

            child = child.Next;
        }
    }

    // Replaces the text of the literal by its text and its dashes. An escaped dash is never the start of a dash.
    private void MarkDashesInLiteral(InlineNode first, InlineNode last)
    {
        var escaped = first.Kind == SyntaxKind.EscapedCharacterToken;
        var start = escaped ? first.End : first.Start;
        var end = last.End;
        var position = _text.AsSpan(start, end - start).IndexOf("--", StringComparison.Ordinal);
        if (position < 0)
        {
            return;
        }

        var parent = first.Parent!;
        var anchor = escaped ? first : first.Previous;
        var token = escaped ? first.Next : first;
        while (token is not null)
        {
            var next = token.Next;
            token.Unlink();
            if (token == last)
            {
                break;
            }

            token = next;
        }

        var textStart = start;
        while (position >= 0)
        {
            var dashStart = start + position;
            var dashEnd = dashStart + (dashStart + 2 < end && _text[dashStart + 2] == '-' ? 3 : 2);
            if (dashStart > textStart)
            {
                anchor = Insert(parent, anchor, NewNode(SyntaxKind.TextToken, textStart, dashStart));
            }

            var dash = NewNode(SyntaxKind.SmartyPantToken, dashStart, dashEnd);
            dash.Value = (int)(dashEnd - dashStart == 3 ? MarkdownSmartyPant.EmDash : MarkdownSmartyPant.EnDash);
            anchor = Insert(parent, anchor, dash);
            textStart = dashEnd;
            start = dashEnd;
            position = _text.AsSpan(start, end - start).IndexOf("--", StringComparison.Ordinal);
        }

        if (textStart < end)
        {
            Insert(parent, anchor, NewNode(SyntaxKind.TextToken, textStart, end));
        }

        static InlineNode Insert(InlineNode parent, InlineNode? anchor, InlineNode node)
        {
            if (anchor is null)
            {
                parent.Prepend(node);
            }
            else
            {
                anchor.InsertAfter(node);
            }

            return node;
        }
    }

    private static InlineNode? GetLastNonAttributes(InlineNode parent)
    {
        var node = parent.LastChild;
        while (node is { Kind: SyntaxKind.GenericAttributes })
        {
            node = node.Previous;
        }

        return node;
    }

    // As Markdig: the end of the text is whitespace, and an ASCII punctuation character or a Unicode one is punctuation
    private static bool IsMathWhitespace(char c) => c is '\0' or ' ' or '\t' or '\n' or '\r' or '\v' or '\f' || (c > 127 && char.IsWhiteSpace(c));

    private static bool IsMathPunctuation(char c) => c == '\0' || (c <= 127 ? char.IsAsciiLetterOrDigit(c) is false && c > ' ' && c != '\x7f' : char.IsPunctuation(c) || char.IsSymbol(c));

    // The extra emphasis of Markdig: ~ when subscript is on, ^, ++, == and the :: of custom containers
    // A delimiter extension can use these characters when their extension is disabled
    private bool IsExtraEmphasis(char c) => c switch
    {
        '~' => _subscript || _syntaxExtensions?.GetDelimiterIndex(c) >= 0,
        '^' or '+' or '=' or ':' or '"' => true,
        _ => _syntaxExtensions?.GetDelimiterIndex(c) >= 0,
    };

    private int GetMinimumCount(char c) => c switch
    {
        _ when _syntaxExtensions?.GetDelimiterIndex(c) is >= 0 and var index => ((MarkdownDelimiterExtension)_syntaxExtensions.Extensions[index]).MinimumCount,
        '~' when _subscript => 1,
        '~' => 2,
        '+' or '=' or ':' or '"' => 2,
        _ => 1,
    };

    private int GetMaximumCount(char c) => c switch
    {
        _ when _syntaxExtensions?.GetDelimiterIndex(c) is >= 0 and var index => ((MarkdownDelimiterExtension)_syntaxExtensions.Extensions[index]).MaximumCount,
        '~' => _strikethrough ? 2 : 1,
        '^' => 1,
        _ => 2,
    };

    private static bool IsPunctuationException(Rune rune) => rune.Value is '\u2212' or '-' or '+' or '\u2020' or '\u2021';

    private static Rune GetFirstRune(string text) => Rune.DecodeFromUtf16(text, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : Rune.ReplacementChar;

    private static Rune GetLastRune(string text) => Rune.DecodeLastFromUtf16(text, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : Rune.ReplacementChar;

    private Rune GetRuneBefore(int index)
    {
        if (index - 2 >= _textStart && char.IsLowSurrogate(_text[index - 1]) && char.IsHighSurrogate(_text[index - 2]))
        {
            return new Rune(_text[index - 2], _text[index - 1]);
        }

        return Rune.IsValid(_text[index - 1]) ? new Rune(_text[index - 1]) : Rune.ReplacementChar;
    }

    private Rune GetRuneAt(int index)
    {
        if (index + 1 < _textEnd && char.IsHighSurrogate(_text[index]) && char.IsLowSurrogate(_text[index + 1]))
        {
            return new Rune(_text[index], _text[index + 1]);
        }

        return Rune.IsValid(_text[index]) ? new Rune(_text[index]) : Rune.ReplacementChar;
    }

    private void ParseOpenBracket(bool image)
    {
        var start = _pos;
        _pos += image ? 2 : 1;
        var node = NewNode(image ? SyntaxKind.ExclamationOpenBracketToken : SyntaxKind.OpenBracketToken, start, _pos);
        _root.Append(node);
        if (_brackets is not null)
        {
            _brackets.BracketAfter = true;
        }

        _brackets = new Bracket(node, _brackets, _delimiters, _pos, image);
        _activeBrackets++;
    }

    private void ParseCloseBracket()
    {
        var closeStart = _pos;
        var opener = _brackets;
        if (opener is null)
        {
            AppendCloseBracketOrEmoji(closeStart);
            return;
        }

        _pos++;

        if (!opener.Active)
        {
            RemoveBracket();
            PromotePipesOfBracket(opener);
            AppendCloseBracketOrEmoji(closeStart);
            return;
        }

        var savePosition = _pos;
        var suffix = new List<InlineNode>();
        var matched = false;
        var footnote = false;

        // An inline link: (destination "title")
        if (PeekIs('('))
        {
            var openParenthesis = _pos;
            _pos++;
            SkipSpacesAndNewline();
            var destinationStart = _pos;
            if (ScanLinkDestination())
            {
                var destinationEnd = _pos;
                var beforeTitle = _pos;
                SkipSpacesAndNewline();
                var titleStart = -1;
                var titleEnd = -1;
                if (_pos > beforeTitle && _pos - 1 >= _textStart && _text[_pos - 1] is ' ' or '\t' or '\n' && ScanLinkTitle() is > 0 and var titleLength)
                {
                    titleStart = _pos;
                    titleEnd = _pos + titleLength;
                    _pos = titleEnd;
                    SkipSpacesAndNewline();
                }

                if (PeekIs(')'))
                {
                    suffix.Add(NewNode(SyntaxKind.OpenParenthesisToken, openParenthesis, openParenthesis + 1));
                    if (destinationEnd > destinationStart)
                    {
                        suffix.Add(NewNode(SyntaxKind.LinkDestinationToken, destinationStart, destinationEnd));
                    }

                    if (titleStart >= 0)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkTitleToken, titleStart, titleEnd);
                    }

                    suffix.Add(NewNode(SyntaxKind.CloseParenthesisToken, _pos, _pos + 1));
                    _pos++;
                    matched = true;
                }
            }

            if (!matched)
            {
                _pos = savePosition;
                suffix.Clear();
            }
        }

        if (!matched)
        {
            // A reference link: [text][label], [text][] or [text]
            var beforeLabel = _pos;
            var labelLength = ScanLinkLabel();
            string? label = null;
            if (labelLength > 2)
            {
                label = _text[(beforeLabel + 1)..(beforeLabel + labelLength - 1)];
            }
            else if (!opener.BracketAfter)
            {
                label = _text[opener.Index..closeStart];
            }

            if (labelLength == 0)
            {
                _pos = savePosition;
            }

            // A label of a footnote makes a reference to the footnote, as in Markdig, an image included
            var normalized = label is null ? null : Scanners.NormalizeLabel(label);
            if (normalized is not null && (_references.ContainsKey(normalized) || (footnote = Footnotes.Contains(normalized)) || (HeadingLabels.Contains(normalized) && !HasActiveBracketBelow(opener))))
            {
                matched = true;
                if (labelLength >= 2)
                {
                    suffix.Add(NewNode(SyntaxKind.OpenBracketToken, beforeLabel, beforeLabel + 1));
                    if (labelLength > 2)
                    {
                        AddLineNodes(suffix, SyntaxKind.LinkLabelToken, beforeLabel + 1, beforeLabel + labelLength - 1);
                    }

                    suffix.Add(NewNode(SyntaxKind.CloseBracketToken, beforeLabel + labelLength - 1, beforeLabel + labelLength));
                    _pos = beforeLabel + labelLength;
                }
            }
        }

        if (!matched)
        {
            RemoveBracket();
            PromotePipesOfBracket(opener);
            AppendCloseBracketOrEmoji(closeStart);
            return;
        }

        var link = NewNode(footnote ? SyntaxKind.FootnoteReference : opener.Image ? SyntaxKind.Image : SyntaxKind.Link, opener.Node.Start, _pos);
        var current = opener.Node.Next;
        while (current is not null)
        {
            var next = current.Next;
            current.Unlink();
            link.Append(current);
            current = next;
        }

        _root.Append(link);
        if (_table is not null)
        {
            RemovePipesOfLink(link);
        }

        ProcessEmphasis(opener.PreviousDelimiter);
        RemoveBracket();

        // The opening bracket is the first token of the link
        opener.Node.Unlink();
        link.Prepend(opener.Node);
        link.Append(NewNode(SyntaxKind.CloseBracketToken, closeStart, closeStart + 1));
        foreach (var node in suffix)
        {
            link.Append(node);
        }

        // There are no links in links. A link deactivated the brackets below an inactive one already, as in cmark.
        if (!opener.Image)
        {
            for (var bracket = _brackets; bracket is not null; bracket = bracket.Previous)
            {
                if (!bracket.Image)
                {
                    if (!bracket.Active)
                    {
                        break;
                    }

                    bracket.Active = false;
                    _activeBrackets--;
                }
            }
        }
    }

    // A bracket that closes no link is text, unless Markdig reads an emoji there, such as ]:)
    private void AppendCloseBracketOrEmoji(int closeStart)
    {
        _pos = closeStart;
        if (_emoji is null || !_emoji.IsFirstCharacter(']') || !ParseEmoji())
        {
            AppendText(closeStart, closeStart + 1);
            _pos = closeStart + 1;
        }
    }

    private void RemoveBracket()
    {
        if (_brackets is { Active: true })
        {
            _activeBrackets--;
        }

        _brackets = _brackets?.Previous;
    }

    // As in Markdig, a heading is not linked to inside a bracket that can still be a link or an image, as in [text [Heading]](url)
    private bool HasActiveBracketBelow(Bracket opener)
    {
        Debug.Assert(opener == _brackets);
        return _activeBrackets > (opener.Active ? 1 : 0);
    }

    // Optional spaces or tabs, including up to one line ending
    private void SkipSpacesAndNewline()
    {
        while (_pos < _end && _text[_pos] is ' ' or '\t')
        {
            _pos++;
        }

        if (_pos < _end && _text[_pos] == '\n')
        {
            _pos++;
            while (_pos < _end && _text[_pos] is ' ' or '\t')
            {
                _pos++;
            }
        }
    }

    // Scans a link destination, which can be empty in angle brackets
    private bool ScanLinkDestination()
    {
        if (PeekIs('<'))
        {
            var braces = Match(Scanners.LinkDestinationBracesRegex());
            _pos += braces;
            return braces > 0;
        }

        var start = _pos;
        var openParentheses = 0;
        while (_pos < _end)
        {
            // Skip the characters that do not end the destination and are not escapes or parentheses
            var index = _text.AsSpan(_pos, _end - _pos).IndexOfAny(LinkDestinationSpecialCharacters);
            if (index < 0)
            {
                _pos = _end;
                break;
            }

            _pos += index;
            var c = _text[_pos];
            if (c == '\\' && _pos + 1 < _end && Scanners.IsEscapable(_text[_pos + 1]))
            {
                _pos += 2;
            }
            else if (c == '(')
            {
                _pos++;
                openParentheses++;
                if (openParentheses > MaximumLinkDestinationParentheses)
                {
                    return false;
                }
            }
            else if (c == ')')
            {
                if (openParentheses < 1)
                {
                    break;
                }

                _pos++;
                openParentheses--;
            }
            // NUL is U+FFFD for CommonMark: it is not a control character
            else if (c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' || (c < ' ' && c != '\0') || c == '\x7f')
            {
                break;
            }
            else
            {
                _pos++;
            }
        }

        if (_pos == start && !PeekIs(')'))
        {
            return false;
        }

        return openParentheses == 0;
    }

    private int ScanLinkTitle() => Match(Scanners.LinkTitleRegex());

    // A link label: brackets around at most 999 characters, where a bracket must be escaped
    private int ScanLinkLabel()
    {
        if (!PeekIs('['))
        {
            return 0;
        }

        var position = _pos + 1;
        while (position < _end && position - _pos <= 1000)
        {
            switch (_text[position])
            {
                case ']':
                    return position + 1 - _pos;
                case '[':
                    return 0;
                case '\\' when position + 1 < _end:
                    position += 2;
                    break;
                default:
                    position++;
                    break;
            }
        }

        return 0;
    }

    private InlineNode AppendText(int start, int end)
    {
        var node = NewNode(SyntaxKind.TextToken, start, end);
        _root.Append(node);
        return node;
    }

    // Adds the tokens of a range that can span lines: a token for each line, and the line endings between them. A token never
    // has a line break, which would put the markers of the containers of the next line in it.
    private void AppendLines(InlineNode parent, SyntaxKind kind, int start, int end)
    {
        var nodes = new List<InlineNode>();
        AddLineNodes(nodes, kind, start, end);
        foreach (var node in nodes)
        {
            parent.Append(node);
        }
    }

    private void AddLineNodes(List<InlineNode> nodes, SyntaxKind kind, int start, int end)
    {
        var lineStart = start;
        for (var i = start; i < end; i++)
        {
            if (_text[i] == '\n')
            {
                nodes.Add(NewNode(kind, lineStart, i));
                nodes.Add(NewNode(SyntaxKind.LineEndingToken, i, i + 1));
                lineStart = i + 1;
            }
        }

        nodes.Add(NewNode(kind, lineStart, end));
    }

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        if (_delimiters == stackBottom)
        {
            return;
        }

        // The lowest delimiter that can open for a closer, by character, whether the closer can open, and length modulo 3
        var openersBottom = new Dictionary<(char, int), Delimiter?>();

        var closer = _delimiters;
        while (closer is not null && closer.Previous != stackBottom)
        {
            closer = closer.Previous;
        }

        while (closer is not null)
        {
            if (!closer.CanClose)
            {
                closer = closer.Next;
                continue;
            }

            // What is left of a run after a match can be too short for its emphasis: it is text
            var extra = IsExtraEmphasis(closer.Character);
            if (extra && closer.Count < GetMinimumCount(closer.Character))
            {
                var next = closer.Next;
                RemoveDelimiter(closer);
                closer = next;
                continue;
            }

            var bottomIndex = (closer.Character, (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3));
            var bottom = openersBottom.GetValueOrDefault(bottomIndex, stackBottom);
            var opener = closer.Previous;
            var openerFound = false;
            while (opener is not null && opener != stackBottom && opener != bottom)
            {
                var oddMatch = (closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 && (opener.OriginalCount + closer.OriginalCount) % 3 == 0;
                if (opener.Character == closer.Character && opener.CanOpen && !oddMatch && (!extra || opener.Count >= GetMinimumCount(opener.Character)))
                {
                    openerFound = true;
                    break;
                }

                opener = opener.Previous;
            }

            var oldCloser = closer;
            if (!openerFound)
            {
                closer = closer.Next;

                // The openers below the closer cannot match the closers that follow it
                openersBottom[bottomIndex] = oldCloser.Previous;
                if (!oldCloser.CanOpen)
                {
                    RemoveDelimiter(oldCloser);
                }

                continue;
            }

            // A strikethrough is between runs of the same length: other runs of tildes are text
            var tilde = closer.Character == '~' && !extra;
            if (tilde && opener!.Count != closer.Count)
            {
                closer = closer.Next;
                RemoveDelimiter(oldCloser);
                RemoveDelimiter(opener);
                continue;
            }

            var use = tilde ? closer.Count
                : extra ? Math.Min(Math.Min(opener!.Count, closer.Count), GetMaximumCount(closer.Character))
                : closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1;
            var openerNode = opener!.Node;
            var closerNode = closer.Node;
            opener.Count -= use;
            closer.Count -= use;

            // The delimiters used are the ones next to the content: the end of the opener and the start of the closer
            var extensionIndex = _syntaxExtensions?.GetDelimiterIndex(closer.Character) ?? -1;
            var kind = closer.Character switch
            {
                _ when extensionIndex >= 0 => SyntaxKind.ExtensionInline,
                '~' when tilde || use == 2 => SyntaxKind.Strikethrough,
                '~' => SyntaxKind.Subscript,
                '^' => SyntaxKind.Superscript,
                '+' => SyntaxKind.Inserted,
                '=' => SyntaxKind.Marked,
                ':' => SyntaxKind.CustomContainerInline,
                '"' => SyntaxKind.Citation,
                _ => use == 1 ? SyntaxKind.Emphasis : SyntaxKind.StrongEmphasis,
            };
            var delimiterKind = kind switch
            {
                SyntaxKind.Strikethrough => SyntaxKind.StrikethroughDelimiterToken,
                SyntaxKind.ExtensionInline => SyntaxKind.ExtensionMarkerToken,
                _ => SyntaxKind.EmphasisDelimiterToken,
            };
            var emphasis = NewNode(kind, openerNode.End - use, closerNode.Start + use);
            var openingDelimiter = NewNode(delimiterKind, openerNode.End - use, openerNode.End);

            // The first token of the construct of a delimiter extension has the index of the extension
            openingDelimiter.Value = extensionIndex + 1;
            emphasis.Append(openingDelimiter);
            openerNode.End -= use;
            var current = openerNode.Next;
            while (current is not null && current != closerNode)
            {
                var next = current.Next;
                current.Unlink();
                emphasis.Append(current);
                current = next;
            }

            emphasis.Append(NewNode(delimiterKind, closerNode.Start, closerNode.Start + use));
            closerNode.Start += use;
            openerNode.InsertAfter(emphasis);

            // The delimiters between the opener and the closer are text now
            var between = closer.Previous;
            while (between is not null && between != opener)
            {
                var previous = between.Previous;
                RemoveDelimiter(between);
                between = previous;
            }

            if (opener.Count == 0)
            {
                openerNode.Unlink();
                RemoveDelimiter(opener);
            }

            if (closer.Count == 0)
            {
                closerNode.Unlink();
                var next = closer.Next;
                RemoveDelimiter(closer);
                closer = next;
            }
        }

        while (_delimiters is not null && _delimiters != stackBottom)
        {
            RemoveDelimiter(_delimiters);
        }
    }

    // An extended www autolink: www. and a domain, after the start of the text, whitespace, or one of *, _, ~ and (
    private bool ParseWwwAutolink()
    {
        if (_brackets is not null || (_pos > _textStart && _text[_pos - 1] is not ('*' or '_' or '~' or '(') && !IsSpace(_text[_pos - 1])))
        {
            return false;
        }

        if (_end - _pos < 4 || !_text.AsSpan(_pos).StartsWith("www.", StringComparison.Ordinal) || CheckDomain(_pos, _end - _pos, allowShort: false) == 0)
        {
            return false;
        }

        var length = AutolinkEnd(_pos, _pos);
        if (length == 0)
        {
            return false;
        }

        AppendAutolink(_pos, _pos + length);
        return true;
    }

    // An extended URL autolink: http://, https:// or ftp://, and a domain. The scheme is already text, which the link takes back.
    private bool ParseUrlAutolink()
    {
        if (_brackets is not null || _end - _pos < 4 || _text[_pos + 1] != '/' || _text[_pos + 2] != '/' || _root.LastChild is not { Kind: SyntaxKind.TextToken } scheme || scheme.End != _pos)
        {
            return false;
        }

        var start = _pos;
        while (start > scheme.Start && char.IsAsciiLetter(_text[start - 1]))
        {
            start--;
        }

        if (!IsSafeScheme(start) || CheckDomain(_pos + 3, _end - _pos - 3, allowShort: true) == 0)
        {
            return false;
        }

        var length = AutolinkEnd(_pos, _pos + 3);
        if (length == 0)
        {
            return false;
        }

        scheme.End = start;
        if (scheme.End == scheme.Start)
        {
            scheme.Unlink();
        }

        AppendAutolink(start, _pos + length);
        return true;
    }

    private bool IsSafeScheme(int start)
    {
        foreach (var scheme in (ReadOnlySpan<string>)["http://", "https://", "ftp://"])
        {
            if (_end - start > scheme.Length && _text.AsSpan(start, scheme.Length).Equals(scheme, StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetterOrDigit(_text[start + scheme.Length]))
            {
                return true;
            }
        }

        return false;
    }

    private void AppendAutolink(int start, int end)
    {
        var link = NewNode(SyntaxKind.ExtendedAutolink, start, end);
        link.Append(NewNode(SyntaxKind.ExtendedAutolinkToken, start, end));
        _root.Append(link);
        _pos = end;
    }

    // The length of the link that starts at data: up to whitespace or <, without the punctuation at its end. Returns 0 when nothing
    // is left.
    private int AutolinkEnd(int data, int from)
    {
        var end = from;
        while (end < _end && !IsSpace(_text[end]) && _text[end] != '<')
        {
            end++;
        }

        return TrimAutolink(_text, data, end - data);
    }

    // Whether the text at data is a domain: segments of characters that are not whitespace or punctuation, or are - or _, separated
    // by dots, with no _ in the last two segments. The first character is not read, and neither is the last one of the
    // paragraph, as in cmark-gfm.
    private int CheckDomain(int data, int size, bool allowShort)
    {
        var dots = 0;
        var underscoresBefore = 0;
        var underscores = 0;
        int i;
        for (i = 1; i < size - 1; i++)
        {
            if (_text[data + i] == '\\' && i < size - 2)
            {
                i++;
            }

            var c = _text[data + i];
            if (c == '_')
            {
                underscores++;
            }
            else if (c == '.')
            {
                underscoresBefore = underscores;
                underscores = 0;
                dots++;
            }
            else if (c != '-' && !IsHostCharacter(c))
            {
                break;
            }
        }

        if (underscoresBefore > 0 || underscores > 0)
        {
            return 0;
        }

        return allowShort || dots > 0 ? i : 0;
    }

    // cmark-gfm reads the domain byte by byte in UTF-8, so it stops after the first byte of a character that is not ASCII
    private static bool IsHostCharacter(char c) => c < 0x80 && !IsSpace(c) && !(c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~'));

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    // Removes the punctuation at the end of a link: ?, !, ., ,, :, *, _, ~, a ) that is not matched, and an entity reference.
    // Returns the new length.
    private static int TrimAutolink(string text, int data, int length)
    {
        var opening = 0;
        var closing = 0;
        for (var i = 0; i < length; i++)
        {
            if (text[data + i] == '(')
            {
                opening++;
            }
            else if (text[data + i] == ')')
            {
                closing++;
            }
        }

        while (length > 0)
        {
            switch (text[data + length - 1])
            {
                case ')':
                    if (closing <= opening)
                    {
                        return length;
                    }

                    closing--;
                    length--;
                    break;

                case '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~':
                    length--;
                    break;

                case ';':
                {
                    // &name; at the end looks like an entity reference
                    var start = length - 2;
                    while (start > 0 && char.IsAsciiLetterOrDigit(text[data + start]))
                    {
                        start--;
                    }

                    length = start < length - 2 && text[data + start] == '&' ? start : length - 1;
                    break;
                }

                default:
                    return length;
            }
        }

        return length;
    }

    // The email addresses of the text become extended autolinks, as cmark-gfm does once the inlines are parsed: a run of text,
    // escaped characters and entities is read as the text it renders as.
    private void LinkEmailAddresses(InlineNode container)
    {
        var run = new List<InlineNode>();
        var child = container.FirstChild;
        while (child is not null)
        {
            var next = child.Next;
            if (child.Kind is SyntaxKind.TextToken or SyntaxKind.EscapedCharacterToken or SyntaxKind.EntityToken)
            {
                run.Add(child);
            }
            else
            {
                LinkEmailAddresses(run);
                run.Clear();
                if (child.Kind is SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis or SyntaxKind.Strikethrough or SyntaxKind.Subscript or SyntaxKind.Superscript or SyntaxKind.Inserted or SyntaxKind.Marked or SyntaxKind.CustomContainerInline or SyntaxKind.Citation or SyntaxKind.Image)
                {
                    LinkEmailAddresses(child);
                }
            }

            child = next;
        }

        LinkEmailAddresses(run);
    }

    private void LinkEmailAddresses(List<InlineNode> run)
    {
        // An address has an @, which an entity can be too
        if (!MayContainAt(run))
        {
            return;
        }

        // The rendered text of the run, and for each of its characters, the token it comes from and its position in a text token
        var text = new StringBuilder();
        var origins = new List<(int Token, int Position)>();
        for (var i = 0; i < run.Count; i++)
        {
            var token = run[i];
            switch (token.Kind)
            {
                case SyntaxKind.TextToken:
                    for (var position = token.Start; position < token.End; position++)
                    {
                        text.Append(_text[position]);
                        origins.Add((i, position));
                    }

                    break;

                case SyntaxKind.EscapedCharacterToken:
                    text.Append(_text[token.End - 1]);
                    origins.Add((i, -1));
                    break;

                default:
                    foreach (var c in HtmlEntities.Decode(_text[token.Start..token.End]))
                    {
                        text.Append(c);
                        origins.Add((i, -1));
                    }

                    break;
            }
        }

        var matches = new List<(int Start, int End)>();
        foreach (var (start, end) in FindEmailAddresses(text.ToString()))
        {
            // A link starts and ends at the edges of the tokens, or in a text token
            var first = origins[start];
            var last = origins[end - 1];
            var startsAtEdge = first.Position >= 0 || start == 0 || origins[start - 1].Token != first.Token;
            var endsAtEdge = last.Position >= 0 || end == origins.Count || origins[end].Token != last.Token;
            if (startsAtEdge && endsAtEdge)
            {
                matches.Add((first.Position >= 0 ? first.Position : run[first.Token].Start, last.Position >= 0 ? last.Position + 1 : run[last.Token].End));
            }
        }

        if (matches.Count == 0)
        {
            return;
        }

        WrapRanges(run, matches, SyntaxKind.ExtendedAutolink);
    }

    // Replaces the tokens of a run by the same tokens split at the edges of the ranges, the ones in a range being the children
    // of a node of the kind
    private void WrapRanges(List<InlineNode> run, List<(int Start, int End)> ranges, SyntaxKind kind)
    {
        var nodes = new List<InlineNode>();
        var rangeIndex = 0;
        InlineNode? wrapper = null;
        foreach (var token in run)
        {
            var position = token.Start;
            while (position < token.End)
            {
                InlineNode piece;
                if (rangeIndex < ranges.Count && position >= ranges[rangeIndex].Start)
                {
                    if (wrapper is null)
                    {
                        wrapper = NewNode(kind, ranges[rangeIndex].Start, ranges[rangeIndex].End);
                        nodes.Add(wrapper);
                    }

                    var end = Math.Min(token.End, ranges[rangeIndex].End);
                    piece = NewNode(token.Kind, position, end);
                    wrapper.Append(piece);
                    position = end;
                    if (position >= ranges[rangeIndex].End)
                    {
                        rangeIndex++;
                        wrapper = null;
                    }
                }
                else
                {
                    var end = rangeIndex < ranges.Count ? Math.Min(token.End, ranges[rangeIndex].Start) : token.End;
                    piece = NewNode(token.Kind, position, end);
                    nodes.Add(piece);
                    position = end;
                }

                piece.IsDelimiterRun = token.IsDelimiterRun;
            }
        }

        var anchor = run[0];
        foreach (var node in nodes)
        {
            anchor.InsertAfter(node);
            anchor = node;
        }

        foreach (var token in run)
        {
            token.Unlink();
        }
    }

    private bool MayContainAt(List<InlineNode> run)
    {
        foreach (var token in run)
        {
            if (token.Kind == SyntaxKind.EntityToken || _text.AsSpan(token.Start, token.End - token.Start).Contains('@'))
            {
                return true;
            }
        }

        return false;
    }

    // The email addresses of a text, as cmark-gfm finds them: letters, digits, ., +, - and _ before an @, and segments of letters,
    // digits, - and _ separated by dots after it, which end with a letter. The address can start with mailto: or xmpp:, which
    // are not after a letter or a digit, and an xmpp: address can have a resource after a /. The link starts at the scheme:
    // cmark-gfm reads on before it, so that .mailto:a@b.c would link to .mailto:a@b.c.
    private static List<(int Start, int End)> FindEmailAddresses(string text)
    {
        var result = new List<(int Start, int End)>();
        var lower = 0;
        var at = text.IndexOf('@', StringComparison.Ordinal);
        while (at >= 0)
        {
            var start = at;
            var isXmpp = false;
            while (start > lower)
            {
                var c = text[start - 1];
                if (char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-' or '_')
                {
                    start--;
                }
                else if (c == ':' && IsScheme(text, lower, start, "mailto:"))
                {
                    start -= "mailto:".Length;
                    break;
                }
                else if (c == ':' && IsScheme(text, lower, start, "xmpp:"))
                {
                    isXmpp = true;
                    start -= "xmpp:".Length;
                    break;
                }
                else
                {
                    break;
                }
            }

            if (start == at)
            {
                lower = at + 1;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            var length = 1;
            var dots = 0;
            var nextAt = -1;
            for (; at + length < text.Length; length++)
            {
                var c = text[at + length];
                if (char.IsAsciiLetterOrDigit(c))
                {
                    continue;
                }

                if (c == '@')
                {
                    nextAt = at + length;
                    break;
                }

                if (c == '.' && at + length + 1 < text.Length && char.IsAsciiLetterOrDigit(text[at + length + 1]))
                {
                    dots++;
                }
                else if (c is not ('-' or '_') && !(c == '/' && isXmpp))
                {
                    break;
                }
            }

            // Another @: the address is the one before it, if any
            if (nextAt >= 0)
            {
                lower = at + 1;
                at = nextAt;
                continue;
            }

            var lastCharacter = text[at + length - 1];
            if (length < 2 || dots == 0 || (!char.IsAsciiLetter(lastCharacter) && lastCharacter != '.'))
            {
                lower = at + length;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            length = TrimAutolink(text, at, length);
            if (length == 0)
            {
                lower = at + 1;
                at = text.IndexOf('@', lower, StringComparison.Ordinal);
                continue;
            }

            result.Add((start, at + length));
            lower = at + length;
            at = text.IndexOf('@', lower, StringComparison.Ordinal);
        }

        return result;

        // The scheme ends at the colon before the position, and is at the lower bound or after a character that is not a letter
        // or a digit
        static bool IsScheme(string text, int lower, int position, string scheme)
        {
            var schemeStart = position - scheme.Length;
            return schemeStart >= lower
                && text.AsSpan(schemeStart, scheme.Length).SequenceEqual(scheme)
                && (schemeStart == lower || !char.IsAsciiLetterOrDigit(text[schemeStart - 1]));
        }
    }

    private void RemoveDelimiter(Delimiter delimiter)
    {
        if (delimiter.Character < _pendingDelimiters.Length)
        {
            _pendingDelimiters[delimiter.Character]--;
        }

        if (delimiter.Previous is not null)
        {
            delimiter.Previous.Next = delimiter.Next;
        }

        if (delimiter.Next is null)
        {
            _delimiters = delimiter.Previous;
        }
        else
        {
            delimiter.Next.Previous = delimiter.Previous;
        }
    }

    // Replaces the inlines nested in more inlines than the limit by their tokens, so that the passes that walk the inlines recursively,
    // and the tree, stay shallow whatever the text, such as thousands of nested emphasis. The tokens of the constructs that are
    // removed are text; the tokens that can be anywhere in an inline, such as entities, keep their kind.
    private void FlattenDeepInlines()
    {
        // An inline nested in more inlines than the limit has more nodes above it
        if (_usedNodes <= _maximumNestingDepth + 1)
        {
            return;
        }

        // The depth of the node: 1 for the children of the block
        var depth = 1;
        var node = _root.FirstChild;
        while (node is not null)
        {
            if (!node.IsToken && node.FirstChild is not null)
            {
                // The inlines that the node is in
                if (depth - 1 <= _maximumNestingDepth)
                {
                    depth++;
                    node = node.FirstChild;
                    continue;
                }

                node = Flatten(node);
            }

            while (node.Next is null)
            {
                node = node.Parent!;
                depth--;
                if (node == _root)
                {
                    return;
                }
            }

            node = node.Next;
        }
    }

    // Replaces a node by its tokens, and returns the last one
    private InlineNode Flatten(InlineNode container)
    {
        var tokens = _flattenedTokens;
        tokens.Clear();
        var node = container.FirstChild!;
        while (true)
        {
            if (node.IsToken)
            {
                tokens.Add(node);
            }
            else if (node.FirstChild is not null)
            {
                node = node.FirstChild;
                continue;
            }

            while (node.Next is null && node != container)
            {
                node = node.Parent!;
            }

            if (node == container)
            {
                break;
            }

            node = node.Next!;
        }

        // A construct always has tokens, such as its delimiters: an empty token keeps the place of one that would have none
        if (tokens.Count == 0)
        {
            tokens.Add(NewNode(SyntaxKind.TextToken, container.Start, container.Start));
        }

        var anchor = container;
        foreach (var token in tokens)
        {
            token.Unlink();
            if (token.Kind is not (SyntaxKind.TextToken or SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken or SyntaxKind.EscapedCharacterToken or SyntaxKind.EntityToken or SyntaxKind.EmojiToken or SyntaxKind.SmartyPantToken))
            {
                token.ChangeKind(SyntaxKind.TextToken);
                token.Value = 0;
            }

            token.IsDelimiterRun = false;
            anchor.InsertAfter(token);
            anchor = token;
        }

        container.Unlink();
        return anchor;
    }

    private void AddBuildElements(InlineNode node, TreeBuffer result)
    {
        if (node.IsToken)
        {
            if (node.End > node.Start)
            {
                result.AddToken(node.Kind, _subject.ToSourceStart(node.Start), _subject.ToSourceEnd(node.End), node.Value);
            }

            return;
        }

        var index = result.StartNode(node.Kind);
        for (var child = node.FirstChild; child is not null; child = child.Next)
        {
            AddBuildElements(child, result);
        }

        result.EndNode(index);
    }

    private InlineNode NewNode(SyntaxKind kind, int start, int end)
    {
        if (_usedNodes == _nodes.Count)
        {
            _nodes.Add(new InlineNode());
        }

        return _nodes[_usedNodes++].Reset(kind, start, end);
    }

    // Adds the tokens of a range of the subject, split at the line breaks
    private void AddRange(List<BuildElement> elements, SyntaxKind kind, int start, int end)
    {
        var lineStart = start;
        for (var i = start; i < end; i++)
        {
            if (_text[i] == '\n')
            {
                elements.Add(BuildElement.Token(kind, _subject.ToSourceStart(lineStart), _subject.ToSourceEnd(i)));
                elements.Add(BuildElement.Token(SyntaxKind.LineEndingToken, _subject.ToSourceStart(i), _subject.ToSourceEnd(i + 1)));
                lineStart = i + 1;
            }
        }

        if (end > lineStart)
        {
            elements.Add(BuildElement.Token(kind, _subject.ToSourceStart(lineStart), _subject.ToSourceEnd(end)));
        }
    }

    private sealed class Delimiter(char character, int count, InlineNode node, bool canOpen, bool canClose)
    {
        public char Character { get; } = character;

        public int Count { get; set; } = count;

        public int OriginalCount { get; } = count;

        public InlineNode Node { get; } = node;

        public bool CanOpen { get; } = canOpen;

        public bool CanClose { get; } = canClose;

        public Delimiter? Previous { get; set; }

        public Delimiter? Next { get; set; }
    }

    /// <param name="Text">The text that was searched.</param>
    /// <param name="From">Where the search started.</param>
    /// <param name="End">Where the search ended.</param>
    /// <param name="Position">Where the first terminator after <paramref name="From"/> starts, or -1 when there is none before <paramref name="End"/>.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct HtmlTerminator(string? Text, int From, int End, int Position);

    /// <param name="Text">The text that was searched.</param>
    /// <param name="TextEnd">The end of the subject.</param>
    /// <param name="Start">Where the URL starts.</param>
    /// <param name="End">Where the URL ends.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct MarkdigUrlScan(string? Text, int TextEnd, int Start, int End);

    private sealed class Bracket(InlineNode node, Bracket? previous, Delimiter? previousDelimiter, int index, bool image)
    {
        public InlineNode Node { get; } = node;

        public Bracket? Previous { get; } = previous;

        public Delimiter? PreviousDelimiter { get; } = previousDelimiter;

        // Where the text of the link starts, after the bracket
        public int Index { get; } = index;

        public bool Image { get; } = image;

        public bool Active { get; set; } = true;

        public bool BracketAfter { get; set; }
    }
}
