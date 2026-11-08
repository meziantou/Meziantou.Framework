namespace Meziantou.Framework.Templating;

/// <summary>Resolves the partial templates and the layouts that a template renders, and writes the code that renders them.</summary>
/// <remarks>
/// Everything is compiled into the class of the template being built. Each partial is a local function of the run method,
/// declared once however many times it is rendered, so a partial can render itself. Each layout is a local function too,
/// and the body and the sections of the template below it are local functions that the layout calls. A layout is nested
/// in the function of the layout that renders it, so that it can call the body and the sections of that layout:
/// <code>
/// Run(__output__, arguments...)
/// {
///     __Layout_1(arguments of the layout directive);
///     void __Body_0() { ... }
///     void __Section_0_0() { ... }
///     void __Layout_1(parameters...) { ... __Body_0(); ... __Section_0_0(); ... }
///     void __Partial_0(parameters...) { ... }
/// }
/// </code>
/// The local functions capture the arguments of the template, so every partial and layout can use them.
/// </remarks>
internal sealed class TemplateComposition
{
    private const string RenderDirectiveName = "render";
    private const string LayoutDirectiveName = "layout";
    private const string RenderBodyDirectiveName = "renderbody";
    private const string SectionDirectiveName = "section";
    private const string EndSectionDirectiveName = "endsection";
    private const string RenderSectionDirectiveName = "rendersection";
    private const string ParameterDirectiveName = "parameter";
    private const string OptionalKeyword = "optional";

    private readonly Template _template;
    private readonly CompositionUnit _root;
    private readonly List<CompositionUnit> _layouts = [];
    private readonly List<CompositionUnit> _partials = [];
    private readonly Dictionary<string, CompositionUnit> _partialsByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<DirectiveBlock, CompositionUnit> _renderTargets = new(ReferenceEqualityComparer.Instance);

    private TemplateComposition(Template template)
    {
        _template = template;
        _root = new CompositionUnit(CompositionUnitKind.Root, template.SourceFileName, [.. template.Blocks]);
    }

    /// <summary>Gets the class member blocks of the template, its layouts and its partials.</summary>
    public IEnumerable<TemplateBlock> ClassMemberBlocks => AllUnits.SelectMany(unit => unit.Blocks).OfType<ClassMemberBlock>();

    private IEnumerable<CompositionUnit> AllUnits => [_root, .. _layouts, .. _partials];

    /// <summary>Loads the partials and the layouts that a template renders.</summary>
    /// <returns><see langword="null"/> when the template uses no composition directive.</returns>
    /// <exception cref="TemplateException">A template cannot be loaded, or the directives are not valid.</exception>
    public static TemplateComposition? Create(Template template)
    {
        if (!template.Blocks.Any(IsCompositionDirective))
            return null;

        var composition = new TemplateComposition(template);
        composition.Resolve();
        return composition;
    }

    private static bool IsCompositionDirective(TemplateBlock block)
    {
        return block is DirectiveBlock directive && (
            IsDirective(directive, RenderDirectiveName) ||
            IsDirective(directive, LayoutDirectiveName) ||
            IsDirective(directive, RenderBodyDirectiveName) ||
            IsDirective(directive, SectionDirectiveName) ||
            IsDirective(directive, EndSectionDirectiveName) ||
            IsDirective(directive, RenderSectionDirectiveName) ||
            IsDirective(directive, ParameterDirectiveName));
    }

    private static bool IsDirective(DirectiveBlock directive, string name)
    {
        return string.Equals(directive.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies the directives of the partials and the layouts to the template being built.</summary>
    public void ApplyDirectives()
    {
        foreach (var unit in AllUnits)
        {
            if (unit == _root)
                continue;

            foreach (var block in unit.Blocks)
            {
                if (block is DirectiveBlock directive)
                {
                    directive.ApplyDirective();
                }
            }
        }
    }

    private void Resolve()
    {
        Analyze(_root);

        // Layout chain: the layout of the template, then the layout of that layout, …
        var layoutNames = new HashSet<string>(StringComparer.Ordinal);
        if (_root.FullName is not null)
        {
            layoutNames.Add(_root.FullName);
        }

        var current = _root;
        while (current.LayoutDirective is { } layoutDirective)
        {
            var fullName = GetFullName(current, current.LayoutName!, layoutDirective);
            if (!layoutNames.Add(fullName))
                throw CreateException(layoutDirective, $"The layout '{current.LayoutName}' renders itself through its layouts.");

            var layout = LoadUnit(CompositionUnitKind.Layout, current.LayoutName!, fullName, layoutDirective);
            layout.Level = current.Level + 1;
            layout.FunctionName = "__Layout_" + layout.Level.ToString(CultureInfo.InvariantCulture);
            layout.Child = current;
            current.Layout = layout;
            Analyze(layout);
            _layouts.Add(layout);
            current = layout;
        }

        // Partials, including the partials of the partials. A partial is loaded once, so a partial that renders
        // itself, directly or not, is not an error: the condition that stops the recursion is up to the template.
        var units = new List<CompositionUnit> { _root };
        units.AddRange(_layouts);
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            foreach (var block in unit.Blocks)
            {
                if (block is not DirectiveBlock directive || !IsDirective(directive, RenderDirectiveName))
                    continue;

                var (name, _) = ParseNameAndArguments(directive);
                var fullName = GetFullName(unit, name, directive);
                if (!_partialsByFullName.TryGetValue(fullName, out var partial))
                {
                    partial = LoadUnit(CompositionUnitKind.Partial, name, fullName, directive);
                    partial.FunctionName = "__Partial_" + _partials.Count.ToString(CultureInfo.InvariantCulture);
                    Analyze(partial);
                    _partialsByFullName.Add(fullName, partial);
                    _partials.Add(partial);
                    units.Add(partial);
                }

                _renderTargets.Add(directive, partial);
            }
        }
    }

    private string GetFullName(CompositionUnit referrer, string name, DirectiveBlock directive)
    {
        var loader = _template.TemplateLoader
            ?? throw CreateException(directive, $"The template '{name}' cannot be loaded because the {nameof(Template.TemplateLoader)} property of the template is not set.");

        return loader.GetFullName(name, referrer.FullName);
    }

    private CompositionUnit LoadUnit(CompositionUnitKind kind, string name, string fullName, DirectiveBlock directive)
    {
        var text = _template.TemplateLoader!.GetText(fullName);
        if (text is null)
        {
            var description = string.Equals(name, fullName, StringComparison.Ordinal) ? $"'{name}'" : $"'{name}' ({fullName})";
            throw CreateException(directive, $"The template {description} was not found.");
        }

        var blocks = _template.ParseBlocks(text);
        foreach (var block in blocks)
        {
            block.SourceFileName = fullName;
        }

        return new CompositionUnit(kind, fullName, blocks);
    }

    /// <summary>Splits the blocks of a template into its body and its sections, and validates its directives.</summary>
    private void Analyze(CompositionUnit unit)
    {
        Section? currentSection = null;
        var rendersBody = false;
        foreach (var block in unit.Blocks)
        {
            if (block is DirectiveBlock directive)
            {
                if (IsDirective(directive, SectionDirectiveName))
                {
                    if (unit.Kind is CompositionUnitKind.Partial)
                        throw CreateException(directive, "A partial template cannot define sections.");

                    if (currentSection is not null)
                        throw CreateException(directive, $"The section '{currentSection.Name}' is not ended: sections cannot be nested.");

                    var name = directive.Value;
                    if (name.Length == 0 || name.AsSpan().ContainsAny([' ', '\t', '\r', '\n']))
                        throw CreateException(directive, "The section directive requires a name, without spaces.");

                    if (unit.Sections.Exists(section => string.Equals(section.Name, name, StringComparison.Ordinal)))
                        throw CreateException(directive, $"The section '{name}' is defined twice.");

                    currentSection = new Section(name, directive, "__Section_" + unit.Level.ToString(CultureInfo.InvariantCulture) + "_" + unit.Sections.Count.ToString(CultureInfo.InvariantCulture));
                    unit.Sections.Add(currentSection);
                    continue;
                }

                if (IsDirective(directive, EndSectionDirectiveName))
                {
                    if (currentSection is null)
                        throw CreateException(directive, "The endsection directive has no matching section directive.");

                    currentSection = null;
                    continue;
                }

                if (IsDirective(directive, LayoutDirectiveName))
                {
                    if (unit.Kind is CompositionUnitKind.Partial)
                        throw CreateException(directive, "A partial template cannot have a layout.");

                    if (currentSection is not null)
                        throw CreateException(directive, "The layout directive cannot be in a section.");

                    if (unit.LayoutDirective is not null)
                        throw CreateException(directive, "A template can only have one layout.");

                    (unit.LayoutName, unit.LayoutArguments) = ParseNameAndArguments(directive);
                    unit.LayoutDirective = directive;
                    continue;
                }

                if (IsDirective(directive, ParameterDirectiveName))
                {
                    if (unit.Kind is CompositionUnitKind.Root)
                        throw CreateException(directive, $"The parameter directive declares a parameter of a partial template or a layout. Declare the arguments of the template with its {nameof(Template.Arguments)} property.");

                    if (currentSection is not null)
                        throw CreateException(directive, "The parameter directive cannot be in a section.");

                    if (directive.Value.Length == 0)
                        throw CreateException(directive, "The parameter directive requires a type and a name.");

                    unit.Parameters.Add(directive);
                    continue;
                }

                if (IsDirective(directive, RenderBodyDirectiveName) || IsDirective(directive, RenderSectionDirectiveName))
                {
                    if (unit.Kind is not CompositionUnitKind.Layout)
                        throw CreateException(directive, $"The {directive.Name} directive can only be used in a layout.");

                    rendersBody |= IsDirective(directive, RenderBodyDirectiveName);
                    if (IsDirective(directive, RenderSectionDirectiveName))
                    {
                        _ = ParseRenderSection(directive);
                    }
                }
                else if (IsDirective(directive, RenderDirectiveName))
                {
                    _ = ParseNameAndArguments(directive);
                }
            }

            (currentSection?.Blocks ?? unit.Body).Add(block);
        }

        if (currentSection is not null)
            throw CreateException(currentSection.Directive, $"The section '{currentSection.Name}' is not ended: add an endsection directive.");

        if (unit.Sections.Count > 0 && unit.LayoutDirective is null)
            throw CreateException(unit.Sections[0].Directive, "Sections can only be defined by a template that has a layout.");

        if (unit.Kind is CompositionUnitKind.Layout && !rendersBody)
            throw CreateException(unit.Child!.LayoutDirective!, $"The layout '{unit.Child.LayoutName}' does not render the body: add a renderbody directive.");
    }

    private (string Name, string Arguments) ParseNameAndArguments(DirectiveBlock directive)
    {
        var value = directive.Value;
        string name;
        string arguments;
        if (value.StartsWith('"', StringComparison.Ordinal))
        {
            var end = value.IndexOf('"', 1, StringComparison.Ordinal);
            if (end < 0)
                throw CreateException(directive, $"The name of the template is not terminated: add a '\"'.");

            name = value[1..end];
            arguments = value[(end + 1)..].Trim();
        }
        else
        {
            var end = value.AsSpan().IndexOfAny([' ', '\t', '\r', '\n']);
            name = end < 0 ? value : value[..end];
            arguments = end < 0 ? string.Empty : value[end..].Trim();
        }

        if (name.Length == 0)
            throw CreateException(directive, $"The {directive.Name} directive requires the name of a template.");

        return (name, arguments);
    }

    private (string Name, bool IsOptional) ParseRenderSection(DirectiveBlock directive)
    {
        var parts = directive.Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return parts switch
        {
            [var name] => (name, false),
            [var name, var keyword] when string.Equals(keyword, OptionalKeyword, StringComparison.OrdinalIgnoreCase) => (name, true),
            _ => throw CreateException(directive, "The rendersection directive requires the name of a section, optionally followed by 'optional'."),
        };
    }

    /// <summary>Writes the statements of the run method: the template, its layouts and its partials.</summary>
    public void WriteRunMethodBody(IndentedTextWriter writer)
    {
        if (_root.Layout is null)
        {
            WriteContent(writer, _root, _root.Body);
        }
        else
        {
            WritePage(writer, _root);
        }

        foreach (var partial in _partials)
        {
            WriteLocalFunction(writer, partial.FunctionName!, partial, () => WriteContent(writer, partial, partial.Body));
        }

        foreach (var unit in AllUnits)
        {
            foreach (var section in unit.Sections)
            {
                if (!section.IsRendered)
                    throw CreateException(section.Directive, $"The section '{section.Name}' is not rendered by the layout '{unit.LayoutName}'.");
            }
        }
    }

    /// <summary>Writes a template that has a layout: the call to the layout, then its body, its sections and its layout as local functions.</summary>
    private void WritePage(IndentedTextWriter writer, CompositionUnit unit)
    {
        var layout = unit.Layout!;
        WriteCall(writer, layout.FunctionName!, unit.LayoutArguments!, unit.LayoutDirective!);

        WriteLocalFunction(writer, GetBodyFunctionName(unit), parameters: null, () => WriteContent(writer, unit, unit.Body));
        foreach (var section in unit.Sections)
        {
            WriteLocalFunction(writer, section.FunctionName, parameters: null, () => WriteContent(writer, unit, section.Blocks));
        }

        WriteLocalFunction(writer, layout.FunctionName!, layout, () =>
        {
            if (layout.Layout is null)
            {
                WriteContent(writer, layout, layout.Body);
            }
            else
            {
                WritePage(writer, layout);
            }
        });
    }

    private void WriteContent(IndentedTextWriter writer, CompositionUnit unit, List<TemplateBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ClassMemberBlock:
                    break;

                case DirectiveBlock directive when IsDirective(directive, RenderDirectiveName):
                    WriteCall(writer, _renderTargets[directive].FunctionName!, ParseNameAndArguments(directive).Arguments, directive);
                    break;

                case DirectiveBlock directive when IsDirective(directive, RenderBodyDirectiveName):
                    writer.WriteLine(GetBodyFunctionName(unit.Child!) + "();");
                    break;

                case DirectiveBlock directive when IsDirective(directive, RenderSectionDirectiveName):
                    var (name, isOptional) = ParseRenderSection(directive);
                    var section = unit.Child!.Sections.Find(section => string.Equals(section.Name, name, StringComparison.Ordinal));
                    if (section is not null)
                    {
                        section.IsRendered = true;
                        writer.WriteLine(section.FunctionName + "();");
                    }
                    else if (!isOptional)
                    {
                        throw CreateException(directive, $"The section '{name}' is required by the layout, but it is not defined. Define it, or add 'optional' to the rendersection directive.");
                    }

                    break;

                default:
                    _template.WriteBlock(writer, block);
                    break;
            }
        }
    }

    private static string GetBodyFunctionName(CompositionUnit unit)
    {
        return "__Body_" + unit.Level.ToString(CultureInfo.InvariantCulture);
    }

    private void WriteLocalFunction(IndentedTextWriter writer, string name, CompositionUnit? parameters, Action writeBody)
    {
        writer.Write("void " + name + "(");
        if (parameters is { Parameters.Count: > 0 })
        {
            // One parameter per line, so that the compiler reports the errors of a declaration at its directive
            writer.WriteLine();
            writer.Indent++;
            for (var i = 0; i < parameters.Parameters.Count; i++)
            {
                var directive = parameters.Parameters[i];
                var code = directive.Value + (i == parameters.Parameters.Count - 1 ? ")" : ",");
                WriteMappedLine(writer, directive, directive.Value, code, codeOffset: 0);
            }

            writer.Indent--;
        }
        else
        {
            writer.WriteLine(")");
        }

        writer.WriteLine("{");
        writer.Indent++;
        writeBody();
        writer.Indent--;
        writer.WriteLine("}");
    }

    /// <summary>Writes the call to a partial or a layout, mapped to the arguments of the directive so that the compiler reports their errors there.</summary>
    private void WriteCall(IndentedTextWriter writer, string functionName, string arguments, DirectiveBlock directive)
    {
        var code = functionName + "(" + arguments + ");";
        if (arguments.Length == 0)
        {
            WriteMappedLine(writer, directive, directive.Text.Trim(), code, codeOffset: 0);
        }
        else
        {
            WriteMappedLine(writer, directive, arguments, code, codeOffset: functionName.Length + 1);
        }
    }

    /// <summary>Writes a line of code, mapped to the text of a directive.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="directive">The directive the code comes from.</param>
    /// <param name="mappedText">The text of the directive that the code is mapped to, a substring of its <see cref="TemplateBlock.Text"/>.</param>
    /// <param name="code">The code.</param>
    /// <param name="codeOffset">The offset of the code that corresponds to the start of <paramref name="mappedText"/>.</param>
    private void WriteMappedLine(IndentedTextWriter writer, DirectiveBlock directive, string mappedText, string code, int codeOffset)
    {
        var textOffset = directive.Text.LastIndexOf(mappedText, StringComparison.Ordinal);
        if (mappedText.Length == 0 || textOffset < 0)
        {
            writer.WriteLine(code);
            return;
        }

        var start = Advance(directive.Start, directive.Text.AsSpan(0, textOffset));
        var end = Advance(start, mappedText);
        var generatedCodeColumnOffset = (writer.Indent * IndentedTextWriter.DefaultTabString.Length) + codeOffset;
        writer.WriteLineNoTabs(Template.CreateLineDirective(directive.SourceFileName ?? _template.SourceFileName, new TextSpan(start, end), generatedCodeColumnOffset));
        writer.WriteLine(code);
        writer.WriteLineNoTabs("#line default");
    }

    private static TextPosition Advance(TextPosition position, ReadOnlySpan<char> text)
    {
        var line = position.Line;
        var column = position.Column;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' || (text[i] == '\n' && (i == 0 || text[i - 1] != '\r')))
            {
                line++;
                column = 1;
            }
            else if (text[i] != '\n')
            {
                column++;
            }
        }

        return new TextPosition(line, column, position.Index + text.Length);
    }

    private TemplateException CreateException(TemplateBlock block, string message)
    {
        var fileName = block.SourceFileName ?? _template.SourceFileName;
        var location = string.Create(CultureInfo.InvariantCulture, $"({block.Start.Line},{block.Start.Column})");
        return new TemplateException(fileName + location + ": " + message);
    }

    private enum CompositionUnitKind
    {
        Root,
        Layout,
        Partial,
    }

    /// <summary>The template being built, one of its layouts, or one of its partials.</summary>
    private sealed class CompositionUnit(CompositionUnitKind kind, string? fullName, List<TemplateBlock> blocks)
    {
        public CompositionUnitKind Kind { get; } = kind;
        public string? FullName { get; } = fullName;
        public List<TemplateBlock> Blocks { get; } = blocks;
        public List<TemplateBlock> Body { get; } = [];
        public List<Section> Sections { get; } = [];
        public List<DirectiveBlock> Parameters { get; } = [];
        public string? FunctionName { get; set; }
        public DirectiveBlock? LayoutDirective { get; set; }
        public string? LayoutName { get; set; }
        public string? LayoutArguments { get; set; }

        /// <summary>The layout of this template.</summary>
        public CompositionUnit? Layout { get; set; }

        /// <summary>For a layout, the template whose body and sections it renders.</summary>
        public CompositionUnit? Child { get; set; }

        /// <summary>0 for the template being built, 1 for its layout, 2 for the layout of its layout, …</summary>
        public int Level { get; set; }
    }

    private sealed class Section(string name, DirectiveBlock directive, string functionName)
    {
        public string Name { get; } = name;
        public DirectiveBlock Directive { get; } = directive;
        public string FunctionName { get; } = functionName;
        public List<TemplateBlock> Blocks { get; } = [];
        public bool IsRendered { get; set; }
    }
}
