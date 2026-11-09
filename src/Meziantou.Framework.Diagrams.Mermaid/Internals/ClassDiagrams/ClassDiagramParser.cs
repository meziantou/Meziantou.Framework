namespace Meziantou.Framework.Diagrams.Mermaid.Internals.ClassDiagrams;

/// <summary>Parses the <c>classDiagram</c> diagrams of Mermaid.</summary>
internal sealed class ClassDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly ClassDiagram _diagram = new();
    private readonly Dictionary<string, ClassDiagramElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassDiagramNamespace> _namespaces = new(StringComparer.Ordinal);
    private readonly HashSet<ClassDiagramClass> _inNamespace = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<ClassDiagramNamespace> _open = new();
    private readonly bool _hierarchicalNamespaces;

    private ClassDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
        _hierarchicalNamespaces = context.Configuration.GetBoolean("class", "hierarchicalNamespaces") ?? true;
    }

    public static ClassDiagram Parse(ParseContext context, int position) => new ClassDiagramParser(context, position).Parse();

    private ClassDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        var keyword = ReadWord();
        if (keyword is not ("classDiagram" or "classDiagram-v2"))
            throw _scanner.Error(keywordStart, $"'{keyword}' is not a class diagram.");

        _scanner.ExpectStatementEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "class");
        _diagram.HideEmptyMembersBox = _context.Configuration.GetBoolean("class", "hideEmptyMembersBox") ?? false;
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_open.Count > 0)
            throw _scanner.Error(_scanner.Text.Length, $"The namespace '{_open.Peek().Id}' is not closed by '}}'.");

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        if (_scanner.Current == '}')
        {
            if (_open.Count is 0)
                throw _scanner.Error("'}' closes no namespace.");

            _scanner.Position++;
            _open.Pop();
            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("direction"))
        {
            _scanner.Position += "direction".Length;
            _scanner.SkipSpaces();
            var start = _scanner.Position;
            _diagram.Direction = MermaidCommon.ParseDirection(ReadWord()) ?? throw _scanner.Error(start, "A direction is expected: TB, TD, BT, LR or RL.");
            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("namespace"))
        {
            ParseNamespace();
            return;
        }

        if (_scanner.StartsWithKeyword("class"))
        {
            ParseClassDeclaration();
            return;
        }

        if (_scanner.StartsWithKeyword("note"))
        {
            ParseNote();
            return;
        }

        if (_scanner.StartsWithKeyword("classDef"))
        {
            var start = _scanner.Position;
            _scanner.Position += "classDef".Length;
            _scanner.SkipSpaces();
            var names = ReadWord();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (_scanner.StartsWithKeyword("cssClass"))
        {
            _scanner.Position += "cssClass".Length;
            _scanner.SkipSpaces();
            if (_scanner.Current != '"')
                throw _scanner.Error("The quoted identifiers of the classes are expected.");

            var ids = _scanner.ReadQuoted();
            _scanner.SkipSpaces();
            var classNameStart = _scanner.Position;
            var className = ReadWord().TrimEnd(';');
            if (className.Length is 0 || !className.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                throw _scanner.Error(classNameStart, "A class name of letters, digits, '_' and '-' is expected.");

            foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (id.AsSpan().IndexOfAny("`\r\n") >= 0)
                    throw _scanner.Error(classNameStart, $"'{id}' is not a valid class name.");

                GetElement(id, _scanner.Position).CssClasses.Add(className);
            }

            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("style"))
        {
            var start = _scanner.Position;
            _scanner.Position += "style".Length;
            _scanner.SkipSpaces();
            var id = ReadClassName();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            var element = GetElement(id, start);
            element.Style = element.Style is null ? style : element.Style.Merge(style);
            return;
        }

        if (_scanner.StartsWithKeyword("click") || _scanner.StartsWithKeyword("link") || _scanner.StartsWithKeyword("callback"))
        {
            ParseInteraction();
            return;
        }

        if (_scanner.StartsWith("<<"))
        {
            var annotation = ReadAnnotation();
            _scanner.SkipSpaces();
            var id = ReadClassName();
            GetClass(id, _scanner.Position).Annotations.Add(annotation);
            _scanner.ExpectStatementEnd();
            return;
        }

        ParseClassStatement();
    }

    /// <summary>Parses <c>Name : member</c>, a relation, or a class alone.</summary>
    private void ParseClassStatement()
    {
        var start = _scanner.Position;
        var name = ReadClassName();
        var generic = ReadGeneric();
        _scanner.SkipSpaces();
        if (_scanner.Current == ':' && !_scanner.StartsWith(":::"))
        {
            _scanner.Position++;
            var member = _scanner.ReadRestOfLine().Trim();
            var item = GetClass(name, start);
            ApplyGeneric(item, generic);
            AddMember(item, member);
            return;
        }

        if (_scanner.StartsWith(":::"))
        {
            var item = GetClass(name, start);
            ApplyGeneric(item, generic);
            ReadCssClasses(item);
            _scanner.ExpectStatementEnd();
            return;
        }

        string? fromCardinality = null;
        if (_scanner.Current == '"')
        {
            fromCardinality = _scanner.ReadQuoted();
            _scanner.SkipSpaces();
        }

        var operatorStart = _scanner.Position;
        if (!TryReadRelation(out var fromEnd, out var dashed, out var toEnd))
        {
            if (fromCardinality is null && _scanner.IsAtStatementEnd())
            {
                ApplyGeneric(GetClass(name, start), generic);
                _scanner.ExpectStatementEnd();
                return;
            }

            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a relation, ':' or the end of the statement is expected.");
        }

        _scanner.SkipSpaces();
        string? toCardinality = null;
        if (_scanner.Current == '"')
        {
            toCardinality = _scanner.ReadQuoted();
            _scanner.SkipSpaces();
        }

        var targetStart = _scanner.Position;
        var targetName = ReadClassName();
        var targetGeneric = ReadGeneric();
        _scanner.SkipSpaces();
        DiagramText? label = null;
        if (_scanner.Current == ':')
        {
            _scanner.Position++;
            var text = _scanner.ReadRestOfLine().Trim();
            label = text.Length is 0 ? null : new DiagramText(MermaidText.DecodePlain(text));
        }
        else
        {
            _scanner.ExpectStatementEnd();
        }

        var from = fromEnd is ClassRelationEnd.Lollipop ? GetInterfaceOrClass(name, start) : GetClass(name, start);
        var to = toEnd is ClassRelationEnd.Lollipop ? GetInterfaceOrClass(targetName, targetStart) : GetClass(targetName, targetStart);
        if (from is ClassDiagramClass fromClass)
        {
            ApplyGeneric(fromClass, generic);
        }

        if (to is ClassDiagramClass toClass)
        {
            ApplyGeneric(toClass, targetGeneric);
        }

        if (_diagram.Relations.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(operatorStart, $"The class diagram has more than {_context.Options.MaxEdges} relations.");

        _diagram.Relations.Add(new ClassRelation(from, to)
        {
            FromEnd = fromEnd,
            ToEnd = toEnd,
            IsDashed = dashed,
            Label = label,
            FromCardinality = fromCardinality is null ? null : MermaidText.DecodeEntities(fromCardinality),
            ToCardinality = toCardinality is null ? null : MermaidText.DecodeEntities(toCardinality),
        });
    }

    /// <summary>Reads a relation: an optional start (<c>&lt;|</c>, <c>*</c>, <c>o</c>, <c>&lt;</c>, <c>()</c>), a line (<c>--</c> or <c>..</c>) and an optional end.</summary>
    private bool TryReadRelation(out ClassRelationEnd fromEnd, out bool dashed, out ClassRelationEnd toEnd)
    {
        var start = _scanner.Position;
        fromEnd = ClassRelationEnd.None;
        toEnd = ClassRelationEnd.None;
        dashed = false;
        if (_scanner.TryConsume("<|"))
        {
            fromEnd = ClassRelationEnd.Inheritance;
        }
        else if (_scanner.TryConsume("()"))
        {
            fromEnd = ClassRelationEnd.Lollipop;
        }
        else if (_scanner.Current is '*' or 'o' or '<' && _scanner.Peek() is '-' or '.')
        {
            fromEnd = _scanner.Current switch
            {
                '*' => ClassRelationEnd.Composition,
                'o' => ClassRelationEnd.Aggregation,
                _ => ClassRelationEnd.Arrow,
            };
            _scanner.Position++;
        }

        if (_scanner.TryConsume("--"))
        {
            dashed = false;
        }
        else if (_scanner.TryConsume(".."))
        {
            dashed = true;
        }
        else
        {
            _scanner.Position = start;
            return false;
        }

        if (_scanner.TryConsume("|>"))
        {
            toEnd = ClassRelationEnd.Inheritance;
        }
        else if (_scanner.TryConsume("()"))
        {
            toEnd = ClassRelationEnd.Lollipop;
        }
        else if (_scanner.Current is '*' or '>' || (_scanner.Current == 'o' && _scanner.Peek() is ' ' or '\t' or '"'))
        {
            toEnd = _scanner.Current switch
            {
                '*' => ClassRelationEnd.Composition,
                'o' => ClassRelationEnd.Aggregation,
                _ => ClassRelationEnd.Arrow,
            };
            _scanner.Position++;
        }

        return true;
    }

    private void ParseClassDeclaration()
    {
        var start = _scanner.Position;
        _scanner.Position += "class".Length;
        _scanner.SkipSpaces();
        var nameStart = _scanner.Position;
        var name = ReadClassName();
        var item = GetClass(name, nameStart);
        if (_open.TryPeek(out var ns) && _inNamespace.Add(item))
        {
            ns.Classes.Add(item);
            _diagram.Classes.Remove(item);
        }

        ApplyGeneric(item, ReadGeneric());
        if (_scanner.Current == '[')
        {
            _scanner.Position++;
            _scanner.SkipSpaces();
            if (_scanner.Current != '"')
                throw _scanner.Error("A quoted label is expected.");

            item.Label = new DiagramText(MermaidText.DecodePlain(_scanner.ReadQuoted()));
            _scanner.SkipSpaces();
            _scanner.Expect("]");
        }

        ApplyGeneric(item, ReadGeneric());
        ReadCssClasses(item);
        _scanner.SkipSpaces();
        if (_scanner.StartsWith("<<"))
        {
            item.Annotations.Add(ReadAnnotation());
            _scanner.SkipSpaces();
        }

        if (_scanner.Current == '{')
        {
            var close = FindClosingBrace(_scanner.Position);
            if (close < 0)
                throw _scanner.Error(start, $"The members of the class '{name}' are not closed by '}}'.");

            foreach (var line in _scanner.Text[(_scanner.Position + 1)..close].Split('\n'))
            {
                var member = line.Trim();
                if (member.Length is 0 || member.StartsWith("%%", StringComparison.Ordinal))
                    continue;

                AddMember(item, member);
            }

            _scanner.Position = close + 1;
        }

        _scanner.ExpectStatementEnd();
    }

    private int FindClosingBrace(int open)
    {
        for (var i = open + 1; i < _scanner.Text.Length; i++)
        {
            if (_scanner.Text[i] == '}')
                return i;
        }

        return -1;
    }

    private static void AddMember(ClassDiagramClass item, string text)
    {
        text = text.Trim();
        if (text.Length is 0)
            return;

        if (text.StartsWith("<<", StringComparison.Ordinal) && text.EndsWith(">>", StringComparison.Ordinal) && text.Length > 4)
        {
            var annotation = MermaidText.DecodeEntities(text[2..^2].Trim());
            if (annotation.Length > 0)
            {
                item.Annotations.Add(annotation);
            }

            return;
        }

        var isStatic = false;
        var isAbstract = false;
        while (text.Length > 1 && text[^1] is '$' or '*')
        {
            if (text[^1] == '$')
            {
                isStatic = true;
            }
            else
            {
                isAbstract = true;
            }

            text = text[..^1].TrimEnd();
        }

        // The classifier of a method can also follow its parentheses, before its return type: run()$ int
        var close = text.AsSpan().LastIndexOf(')');
        while (close >= 0 && close + 1 < text.Length && text[close + 1] is '$' or '*')
        {
            if (text[close + 1] == '$')
            {
                isStatic = true;
            }
            else
            {
                isAbstract = true;
            }

            text = text.Remove(close + 1, 1);
        }

        var visibility = text[0] switch
        {
            '+' => ClassMemberVisibility.Public,
            '-' => ClassMemberVisibility.Private,
            '#' => ClassMemberVisibility.Protected,
            '~' => ClassMemberVisibility.Package,
            _ => ClassMemberVisibility.None,
        };

        if (visibility is not ClassMemberVisibility.None)
        {
            text = text[1..].TrimStart();
        }

        if (text.Length is 0)
            return;

        item.Members.Add(new ClassMember(MermaidText.DecodeEntities(text)) { Visibility = visibility, IsStatic = isStatic, IsAbstract = isAbstract });
    }

    private void ParseNamespace()
    {
        if (_open.Count >= _context.Options.MaxDepth)
            throw _scanner.Error($"The namespaces are nested deeper than {_context.Options.MaxDepth} levels.");

        _scanner.Position += "namespace".Length;
        _scanner.SkipSpaces();
        var idStart = _scanner.Position;
        var id = ReadClassName();
        DiagramText? label = null;
        if (_scanner.Current == '[')
        {
            _scanner.Position++;
            _scanner.SkipSpaces();
            if (_scanner.Current != '"')
                throw _scanner.Error("A quoted label is expected.");

            label = new DiagramText(MermaidText.DecodePlain(_scanner.ReadQuoted()));
            _scanner.SkipSpaces();
            _scanner.Expect("]");
        }

        _scanner.SkipSpaces();
        _scanner.Expect("{");
        _scanner.ExpectStatementEnd();
        var ns = GetNamespace(id, idStart);
        if (label is not null)
        {
            ns.Label = label;
        }

        if (_open.TryPeek(out var parent) && !ContainsNamespace(parent, ns))
        {
            RemoveNamespace(ns);
            parent.Namespaces.Add(ns);
        }

        _open.Push(ns);
    }

    private ClassDiagramNamespace GetNamespace(string id, int offset)
    {
        if (_namespaces.TryGetValue(id, out var existing))
            return existing;

        if (_namespaces.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The class diagram has more than {_context.Options.MaxNodes} namespaces.");

        var dot = id.AsSpan().LastIndexOf('.');
        var ns = new ClassDiagramNamespace(id, _hierarchicalNamespaces && dot > 0 && dot < id.Length - 1 ? new DiagramText(id[(dot + 1)..]) : null);
        _namespaces[id] = ns;

        // A.B.C belongs to A.B, which is created when missing
        if (_hierarchicalNamespaces && dot > 0 && dot < id.Length - 1 && _open.Count is 0)
        {
            GetNamespace(id[..dot], offset).Namespaces.Add(ns);
        }
        else if (_open.Count is 0)
        {
            _diagram.Namespaces.Add(ns);
        }

        return ns;
    }

    private static bool ContainsNamespace(ClassDiagramNamespace parent, ClassDiagramNamespace child) => parent.Namespaces.Contains(child);

    private void RemoveNamespace(ClassDiagramNamespace ns)
    {
        _diagram.Namespaces.Remove(ns);
        foreach (var other in _namespaces.Values)
        {
            other.Namespaces.Remove(ns);
        }
    }

    private void ParseNote()
    {
        var start = _scanner.Position;
        _scanner.Position += "note".Length;
        _scanner.SkipSpaces();
        ClassDiagramClass? target = null;
        if (_scanner.StartsWithKeyword("for"))
        {
            _scanner.Position += 3;
            _scanner.SkipSpaces();
            target = GetClass(ReadClassName(), _scanner.Position);
            _scanner.SkipSpaces();
        }

        if (_scanner.Current != '"')
            throw _scanner.Error("The quoted text of the note is expected.");

        var text = new DiagramText(MermaidText.DecodePlain(_scanner.ReadQuoted()));
        _scanner.ExpectStatementEnd();
        if (_diagram.Notes.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The class diagram has more than {_context.Options.MaxNodes} notes.");

        _diagram.Notes.Add(new ClassDiagramNote(text, target));
    }

    private void ParseInteraction()
    {
        var start = _scanner.Position;
        var keyword = ReadWord();
        _scanner.SkipSpaces();
        var idStart = _scanner.Position;
        var element = GetElement(ReadClassName(), idStart);
        _scanner.SkipSpaces();
        string? link = null;
        string? tooltip = null;
        var isLink = keyword is "link" || (keyword is "click" && (_scanner.StartsWithKeyword("href") || _scanner.Current == '"'));
        if (isLink)
        {
            _scanner.TryConsume("href");
            _scanner.SkipSpaces();
            if (_scanner.Current != '"')
                throw _scanner.Error("A quoted address is expected.");

            link = MermaidCommon.SanitizeUrl(MermaidText.DecodeEntities(_scanner.ReadQuoted()), _context, start);
        }
        else
        {
            _context.Warn(start, "The JavaScript callbacks are not supported.");
            _scanner.TryConsume("call");
            _scanner.SkipSpaces();
            if (_scanner.Current == '"')
            {
                _scanner.ReadQuoted();
            }
            else
            {
                while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';' or '"' or ' ' or '\t'))
                {
                    if (_scanner.Current == '(')
                    {
                        var close = _scanner.Text.IndexOfOrdinal(')', _scanner.Position);
                        _scanner.Position = close < 0 ? _scanner.Text.Length : close + 1;
                        continue;
                    }

                    _scanner.Position++;
                }
            }
        }

        _scanner.SkipSpaces();
        if (_scanner.Current == '"')
        {
            tooltip = MermaidText.DecodeEntities(_scanner.ReadQuoted());
            _scanner.SkipSpaces();
        }

        if (_scanner.Current == '_')
        {
            ReadWord();
        }

        _scanner.ExpectStatementEnd();
        if (link is not null)
        {
            element.Link = link;
        }

        if (tooltip is not null)
        {
            element.Tooltip = tooltip;
        }
    }

    private void ReadCssClasses(ClassDiagramClass item)
    {
        while (_scanner.TryConsume(":::"))
        {
            var start = _scanner.Position;
            while (!_scanner.IsEnd && (char.IsLetterOrDigit(_scanner.Current) || _scanner.Current is '_' or '-'))
            {
                _scanner.Position++;
            }

            if (_scanner.Position == start)
                throw _scanner.Error("A class name is expected after ':::'.");

            item.CssClasses.Add(_scanner.Text[start.._scanner.Position]);
        }
    }

    private string ReadAnnotation()
    {
        var start = _scanner.Position;
        _scanner.Expect("<<");
        var end = _scanner.Text.IndexOf(">>", _scanner.Position, StringComparison.Ordinal);
        var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
        if (end < 0 || (lineEnd >= 0 && lineEnd < end))
            throw _scanner.Error(start, "The annotation is not closed by '>>'.");

        var annotation = MermaidText.DecodeEntities(_scanner.Text[_scanner.Position..end].Trim());
        _scanner.Position = end + 2;
        if (annotation.Length is 0)
            throw _scanner.Error(start, "The annotation is empty.");

        return annotation;
    }

    /// <summary>Reads a name: letters, digits, <c>_</c>, <c>-</c> and <c>.</c> (not doubled), or any text between backticks.</summary>
    private string ReadClassName()
    {
        var start = _scanner.Position;
        if (_scanner.Current == '`')
        {
            var end = _scanner.Text.IndexOfOrdinal('`', start + 1);
            var lineEnd = _scanner.Text.IndexOfOrdinal('\n', start);
            if (end < 0 || (lineEnd >= 0 && lineEnd < end) || end == start + 1)
                throw _scanner.Error(start, "The name is not closed by '`'.");

            if (_scanner.Text.AsSpan(start + 1, end - start - 1).IndexOfAny(',', '"') >= 0)
                throw _scanner.Error(start, "A name cannot contain ',' or '\"'.");

            _scanner.Position = end + 1;
            var quoted = _scanner.Text[(start + 1)..end].Trim();
            if (quoted.Length is 0)
                throw _scanner.Error(start, "The name is empty.");

            return quoted;
        }

        var text = _scanner.Text;
        var i = start;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_' || (c is '-' or '.' && i + 1 < text.Length && text[i + 1] != c && text[i + 1] is not ('-' or '.' or '|' or '>' or '*' or 'o' or '(') && i > start))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c) && i + 1 < text.Length && char.IsSurrogatePair(c, text[i + 1]))
            {
                i += 2;
                continue;
            }

            break;
        }

        if (i == start)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a class name is expected.");

        _scanner.Position = i;
        return text[start..i];
    }

    /// <summary>Reads a generic type, <c>~T~</c>, whose tildes can nest: <c>~List~int~~</c>.</summary>
    private string? ReadGeneric()
    {
        if (_scanner.Current != '~')
            return null;

        var start = _scanner.Position;
        var depth = 0;
        var i = start;
        for (; i < _scanner.Text.Length && _scanner.Text[i] != '\n'; i++)
        {
            if (_scanner.Text[i] != '~')
                continue;

            var next = i + 1 < _scanner.Text.Length ? _scanner.Text[i + 1] : ' ';
            if (i == start || char.IsLetterOrDigit(next) || next == '_')
            {
                if (i == start || char.IsLetterOrDigit(_scanner.Text[i - 1]))
                {
                    depth++;
                    continue;
                }
            }

            depth--;
            if (depth == 0)
                break;
        }

        if (depth != 0 || i >= _scanner.Text.Length || _scanner.Text[i] != '~')
            throw _scanner.Error(start, "The generic type is not closed by '~'.");

        _scanner.Position = i + 1;
        var generic = _scanner.Text[(start + 1)..i];
        return generic.Length is 0 ? null : generic;
    }

    private static void ApplyGeneric(ClassDiagramClass item, string? generic)
    {
        if (generic is not null)
        {
            item.GenericType = generic;
        }
    }

    private string ReadWord()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current) && _scanner.Current != ';')
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private ClassDiagramElement GetElement(string id, int offset) => _elements.TryGetValue(id, out var element) ? element : GetClass(id, offset);

    private ClassDiagramElement GetInterfaceOrClass(string id, int offset)
    {
        if (_elements.TryGetValue(id, out var element))
            return element;

        CheckCount(offset);
        var result = new ClassDiagramInterface(id);
        _elements[id] = result;
        _diagram.Interfaces.Add(result);
        return result;
    }

    private ClassDiagramClass GetClass(string id, int offset)
    {
        if (_elements.TryGetValue(id, out var element))
        {
            if (element is ClassDiagramClass existing)
                return existing;

            throw _scanner.Error(offset, $"'{id}' is an interface, not a class.");
        }

        CheckCount(offset);
        var result = new ClassDiagramClass(id);
        _elements[id] = result;
        _diagram.Classes.Add(result);
        return result;
    }

    private void CheckCount(int offset)
    {
        if (_elements.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The class diagram has more than {_context.Options.MaxNodes} classes.");
    }
}
