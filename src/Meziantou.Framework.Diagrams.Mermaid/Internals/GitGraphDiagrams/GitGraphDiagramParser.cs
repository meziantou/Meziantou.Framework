using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.GitGraphDiagrams;

/// <summary>
/// Parses the <c>gitGraph</c> diagrams of Mermaid, and checks the operations as Mermaid does: a branch that does not
/// exist, a merge of a branch into itself or a cherry-pick of an unknown commit are errors.
/// </summary>
internal sealed class GitGraphDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly GitGraphDiagram _diagram = new();
    private readonly State _state;

    private GitGraphDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
        _state = new State();
    }

    public static GitGraphDiagram Parse(ParseContext context, int position) => new GitGraphDiagramParser(context, position).Parse();

    private GitGraphDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("gitGraph"))
            throw _scanner.Error(keywordStart, "'gitGraph' is expected.");

        _scanner.SkipSpaces();
        if (!_scanner.TryConsume(":"))
        {
            var directionStart = _scanner.Position;
            var direction = ReadWord();
            if (direction.Length > 0)
            {
                _diagram.Orientation = direction switch
                {
                    "LR" => GitGraphOrientation.LeftToRight,
                    "TB" => GitGraphOrientation.TopToBottom,
                    "BT" => GitGraphOrientation.BottomToTop,
                    _ => throw _scanner.Error(directionStart, $"'{direction}' is not a direction: use LR, TB or BT."),
                };
                _scanner.SkipSpaces();
                _scanner.Expect(":");
            }
        }

        ExpectLineEnd();
        ApplyConfiguration();
        _state.Reset(_diagram.MainBranchName);
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "gitGraph");
        var configuration = _context.Configuration;
        if (configuration.GetString("gitGraph", "mainBranchName") is { } mainBranchName && mainBranchName.Trim().Length > 0)
        {
            _diagram.MainBranchName = mainBranchName.Trim();
        }

        if (configuration.GetNumber("gitGraph", "mainBranchOrder") is { } order && double.IsFinite(order))
        {
            _diagram.MainBranchOrder = order;
        }

        _diagram.ShowBranches = configuration.GetBoolean("gitGraph", "showBranches") ?? true;
        _diagram.ShowCommitLabels = configuration.GetBoolean("gitGraph", "showCommitLabel") ?? true;
        _diagram.RotateCommitLabels = configuration.GetBoolean("gitGraph", "rotateCommitLabel") ?? true;
        _diagram.ParallelCommits = configuration.GetBoolean("gitGraph", "parallelCommits") ?? false;
        for (var i = 0; i < 8; i++)
        {
            var suffix = i.ToString(CultureInfo.InvariantCulture);
            ReadColor("git" + suffix, color => _diagram.BranchColors[i] = color);
            ReadColor("gitInv" + suffix, color => _diagram.HighlightColors[i] = color);
            ReadColor("gitBranchLabel" + suffix, color => _diagram.BranchLabelColors[i] = color);
        }

        ReadColor("commitLabelColor", color => _diagram.CommitLabelColor = color);
        ReadColor("commitLabelBackground", color => _diagram.CommitLabelBackground = color);
        ReadColor("tagLabelColor", color => _diagram.TagLabelColor = color);
        ReadColor("tagLabelBackground", color => _diagram.TagLabelBackground = color);
        ReadColor("tagLabelBorder", color => _diagram.TagLabelBorder = color);

        void ReadColor(string name, Action<SceneColor> set)
        {
            if (configuration.GetString("themeVariables", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
            {
                set(color);
            }
        }
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram) || GitGraphLangium.TryParseTitle(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        var keyword = ReadWord();
        switch (keyword)
        {
            case "commit":
                ParseCommit(start);
                break;
            case "branch":
                ParseBranch(start);
                break;
            case "checkout" or "switch":
                {
                    _scanner.SkipSpaces();
                    var branch = ReadReference();
                    ExpectLineEnd();
                    if (!_state.Heads.ContainsKey(branch))
                        throw _scanner.Error(start, $"Trying to checkout branch '{branch}', which is not yet created. (Help: try using \"branch {branch}\")");

                    _state.Checkout(branch);
                    Add(new GitGraphCheckout(branch), start);
                    break;
                }

            case "merge":
                ParseMerge(start);
                break;
            case "cherry-pick":
                ParseCherryPick(start);
                break;
            default:
                throw _scanner.Error(start, $"Unexpected '{(keyword.Length is 0 ? _scanner.Describe() : keyword)}': a statement such as 'commit' or 'branch' is expected.");
        }
    }

    private void ParseCommit(int start)
    {
        var commit = new GitGraphCommit();
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd())
                break;

            var attributeStart = _scanner.Position;
            if (_scanner.Current is '"' or '\'')
            {
                commit.Message = GitGraphLangium.ReadString(_scanner);
                continue;
            }

            switch (ReadAttributeName())
            {
                case "id:":
                    commit.Id = ReadString();
                    break;
                case "msg:":
                    commit.Message = ReadString();
                    break;
                case "tag:":
                    commit.Tags.Add(ReadString());
                    break;
                case "type:":
                    commit.Type = ReadType();
                    break;
                default:
                    throw _scanner.Error(attributeStart, $"Unexpected '{_scanner.Describe()}': 'id:', 'msg:', 'tag:' or 'type:' is expected.");
            }
        }

        ExpectLineEnd();
        var id = commit.Id ?? GenerateId(_state.Sequence);
        if (_state.Commits.ContainsKey(id))
        {
            _context.Warn(start, $"The commit '{id}' already exists.");
        }

        _state.AddCommit(id, isMerge: false);
        Add(commit, start);
    }

    private void ParseBranch(int start)
    {
        _scanner.SkipSpaces();
        var name = ReadReference();
        var branch = new GitGraphBranch(name);
        _scanner.SkipSpaces();
        if (_scanner.TryConsume("order:"))
        {
            _scanner.SkipSpaces();
            var orderStart = _scanner.Position;
            var digits = ReadWord();
            if (digits.Length is 0 || !digits.All(char.IsAsciiDigit) || (digits.Length > 1 && digits[0] == '0') || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var order))
                throw _scanner.Error(orderStart, "The order of a branch is a number.");

            branch.Order = order;
        }

        ExpectLineEnd();
        if (_state.Heads.ContainsKey(name))
            throw _scanner.Error(start, $"Trying to create an existing branch '{name}'. (Help: either use a new name if you want to create a new branch, or try using \"checkout {name}\")");

        if (_state.Heads.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} branches.");

        _state.Heads[name] = _state.Head;
        _state.Checkout(name);
        Add(branch, start);
    }

    private void ParseMerge(int start)
    {
        _scanner.SkipSpaces();
        var name = ReadReference();
        var merge = new GitGraphMerge(name);
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd())
                break;

            var attributeStart = _scanner.Position;
            switch (ReadAttributeName())
            {
                case "id:":
                    merge.Id = ReadString();
                    break;
                case "tag:":
                    merge.Tags.Add(ReadString());
                    break;
                case "type:":
                    merge.Type = ReadType();
                    break;
                default:
                    throw _scanner.Error(attributeStart, $"Unexpected '{_scanner.Describe()}': 'id:', 'tag:' or 'type:' is expected.");
            }
        }

        ExpectLineEnd();
        var current = _state.Head;
        var other = _state.Heads.GetValueOrDefault(name);
        if (current is not null && other is not null && current.Branch == name)
            throw _scanner.Error(start, $"Cannot merge branch '{name}' into itself.");

        if (_state.CurrentBranch == name)
            throw _scanner.Error(start, "Incorrect usage of \"merge\". Cannot merge a branch to itself.");

        if (current is null)
            throw _scanner.Error(start, $"Incorrect usage of \"merge\". The current branch ({_state.CurrentBranch}) has no commits.");

        if (!_state.Heads.ContainsKey(name))
            throw _scanner.Error(start, $"Incorrect usage of \"merge\". The branch to merge ({name}) does not exist.");

        if (other is null)
            throw _scanner.Error(start, $"Incorrect usage of \"merge\". The branch to merge ({name}) has no commits.");

        if (current == other)
            throw _scanner.Error(start, "Incorrect usage of \"merge\". Both branches have the same head.");

        if (merge.Id is not null && _state.Commits.ContainsKey(merge.Id))
            throw _scanner.Error(start, $"Incorrect usage of \"merge\". The commit '{merge.Id}' already exists: use another identifier.");

        _state.AddCommit(merge.Id ?? GenerateId(_state.Sequence), isMerge: true, other);
        Add(merge, start);
    }

    private void ParseCherryPick(int start)
    {
        string? id = null;
        var cherryPick = new List<string>();
        string? parent = null;
        var hasTag = false;
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd())
                break;

            var attributeStart = _scanner.Position;
            switch (ReadAttributeName())
            {
                case "id:":
                    id = ReadString();
                    break;
                case "tag:":
                    cherryPick.Add(ReadString());
                    hasTag = true;
                    break;
                case "parent:":
                    parent = ReadString();
                    break;
                default:
                    throw _scanner.Error(attributeStart, $"Unexpected '{_scanner.Describe()}': 'id:', 'tag:' or 'parent:' is expected.");
            }
        }

        ExpectLineEnd();
        if (string.IsNullOrEmpty(id) || !_state.Commits.TryGetValue(id, out var source))
            throw _scanner.Error(start, "Incorrect usage of \"cherry-pick\". The source commit should exist and be provided.");

        if (!string.IsNullOrEmpty(parent) && !source.Parents.Any(item => item.Id == parent))
            throw _scanner.Error(start, "Invalid operation: the specified parent commit is not an immediate parent of the cherry-picked commit.");

        if (source.IsMerge && string.IsNullOrEmpty(parent))
            throw _scanner.Error(start, "Incorrect usage of \"cherry-pick\". If the source commit is a merge commit, an immediate parent commit must be specified.");

        if (source.Branch == _state.CurrentBranch)
            throw _scanner.Error(start, "Incorrect usage of \"cherry-pick\". The source commit is already on the current branch.");

        if (_state.Head is null)
            throw _scanner.Error(start, $"Incorrect usage of \"cherry-pick\". The current branch ({_state.CurrentBranch}) has no commits.");

        var operation = new GitGraphCherryPick(id) { Parent = string.IsNullOrEmpty(parent) ? null : parent, ShowDefaultTag = !hasTag };
        foreach (var tag in cherryPick)
        {
            if (tag.Length > 0)
            {
                operation.Tags.Add(tag);
            }
        }

        _state.AddCommit(GenerateId(_state.Sequence), isMerge: false, source);
        Add(operation, start);
    }

    private void Add(GitGraphOperation operation, int offset)
    {
        if (_diagram.Operations.Count >= _context.Options.MaxEdges * 4)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxEdges * 4} operations.");

        _diagram.Operations.Add(operation);
    }

    private GitGraphCommitType ReadType()
    {
        _scanner.SkipSpaces();
        var start = _scanner.Position;
        return ReadWord() switch
        {
            "NORMAL" => GitGraphCommitType.Normal,
            "REVERSE" => GitGraphCommitType.Reverse,
            "HIGHLIGHT" => GitGraphCommitType.Highlight,
            var word => throw _scanner.Error(start, $"'{word}' is not a type of commit: use NORMAL, REVERSE or HIGHLIGHT."),
        };
    }

    private string ReadString()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted string is expected.");

        return GitGraphLangium.ReadString(_scanner);
    }

    /// <summary>Reads the name of an attribute with its colon, such as <c>id:</c>.</summary>
    private string ReadAttributeName()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiLetter(_scanner.Current))
        {
            _scanner.Position++;
        }

        if (_scanner.Current == ':')
        {
            _scanner.Position++;
            return _scanner.Text[start.._scanner.Position];
        }

        _scanner.Position = start;
        return string.Empty;
    }

    /// <summary>Reads the name of a branch: a quoted string, or letters, digits, <c>_</c>, <c>-</c>, <c>.</c> and <c>/</c> that do not end with <c>.</c> nor <c>/</c>.</summary>
    private string ReadReference()
    {
        var start = _scanner.Position;
        string name;
        if (_scanner.Current is '"' or '\'')
        {
            name = GitGraphLangium.ReadString(_scanner);
        }
        else
        {
            var end = start;
            while (end < _scanner.Text.Length && (char.IsAsciiLetterOrDigit(_scanner.Text[end]) || _scanner.Text[end] is '_' or '-' or '.' or '/'))
            {
                end++;
            }

            while (end > start && _scanner.Text[end - 1] is '.' or '/')
            {
                end--;
            }

            name = _scanner.Text[start..end];
            if (name.Length is 0 || name[0] is '-' or '.' or '/' || IsKeyword(name))
                throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': the name of a branch is expected.");

            _scanner.Position = end;
        }

        if (name.Length is 0)
            throw _scanner.Error(start, "The name of a branch cannot be empty.");

        return name;
    }

    /// <summary>Indicates whether a name is a reference that the grammar reads without quotes.</summary>
    internal static bool IsReference(string name)
    {
        if (name.Length is 0 || !(char.IsAsciiLetterOrDigit(name[0]) || name[0] == '_') || name[^1] is '.' or '/' || IsKeyword(name))
            return false;

        return name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/');
    }

    private static bool IsKeyword(string name) => name is "commit" or "branch" or "merge" or "checkout" or "switch" or "cherry-pick" or "gitGraph" or "LR" or "TB" or "BT" or "NORMAL" or "REVERSE" or "HIGHLIGHT" or "title" or "accTitle" or "accDescr";

    private string ReadWord()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current is '-' or '_'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }

    /// <summary>The commits and the branches the operations made so far, to check the next operations as Mermaid does.</summary>
    private sealed class State
    {
        public Dictionary<string, Commit?> Heads { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Commit> Commits { get; } = new(StringComparer.Ordinal);

        public string CurrentBranch { get; private set; } = string.Empty;

        public Commit? Head { get; private set; }

        public int Sequence { get; private set; }

        public void Reset(string mainBranch)
        {
            Heads[mainBranch] = null;
            CurrentBranch = mainBranch;
        }

        public void Checkout(string branch)
        {
            CurrentBranch = branch;
            Head = Heads[branch];
        }

        public void AddCommit(string id, bool isMerge, Commit? secondParent = null)
        {
            List<Commit> parents = Head is null ? [] : secondParent is null ? [Head] : [Head, secondParent];
            var commit = new Commit(id, CurrentBranch, isMerge, parents);
            Sequence++;
            Commits[id] = commit;
            Head = commit;
            Heads[CurrentBranch] = commit;
        }
    }

    /// <summary>Returns the identifier of a commit without one, as the drawing of the diagrams does.</summary>
    internal static string GenerateId(int sequence)
    {
        var hash = unchecked((uint)(sequence + 1) * 2654435761u);
        return sequence.ToString(CultureInfo.InvariantCulture) + "-" + (hash & 0xFFFFFFF).ToString("x7", CultureInfo.InvariantCulture);
    }

    private sealed record Commit(string Id, string Branch, bool IsMerge, List<Commit> Parents);
}
