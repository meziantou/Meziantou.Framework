using System.Runtime.CompilerServices;
using System.Xml;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>What a document is loaded for, which determines the request (see <see cref="IXsltDocumentLoader"/>).</summary>
internal enum XsltLoadPurpose
{
    /// <summary>The stylesheet of an xsl:include or an xsl:import.</summary>
    Stylesheet,

    /// <summary>A document of the document() function.</summary>
    Document,
}

/// <summary>Loads the stylesheets of xsl:include and xsl:import, and the documents of the document() function.</summary>
internal interface IXsltDocumentLoader
{
    /// <summary>Returns the document of the URL (its fragment removed), or null when it cannot be loaded or is not well-formed XML.</summary>
    Document? Load(UrlRecord url, XsltLoadPurpose purpose);
}

/// <summary>The state of a compilation shared with the contexts of the expressions.</summary>
internal sealed class XsltCompilation
{
    public NameTable NameTable { get; } = new();

    public HashSet<XsltQName> GlobalNames { get; } = [];

    public bool HasGlobalVariable(XsltQName name) => GlobalNames.Contains(name);
}

/// <summary>Compiles a stylesheet from the DOM (https://www.w3.org/TR/xslt-10/#section-Stylesheet-Structure).</summary>
internal sealed class XsltCompiler
{
    private const string XsltNamespace = XsltStylesheet.XsltNamespace;

    // The limits of the stylesheet modules (xsl:include and xsl:import) and of the nesting of the elements
    private const int MaxModuleDepth = 32;
    private const int MaxModules = 256;
    private const int MaxNestingDepth = 1024;

    private readonly XsltStylesheet _stylesheet = new();
    private readonly XsltCompilation _compilation = new();
    private readonly IXsltDocumentLoader? _loader;
    private readonly Dictionary<Element, XsltNamespaceScope> _namespaceScopes = [];
    private readonly HashSet<Element> _simplifiedRoots = [];
    private readonly Dictionary<string, (string? Prefix, string? NamespaceUri)> _namespaceAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<XsltQName, int> _namedTemplatePrecedences = [];
    private int _position;
    private int _moduleCount;
    private int _nestingDepth;

    private XsltCompiler(IXsltDocumentLoader? loader)
    {
        _loader = loader;
    }

    /// <summary>Compiles the stylesheet of a document, or whose root is an element (a stylesheet embedded in a document).</summary>
    /// <exception cref="XsltException">The stylesheet is not valid, or an included or imported stylesheet cannot be loaded.</exception>
    public static XsltStylesheet Compile(Node node, IXsltDocumentLoader? loader)
    {
        var compiler = new XsltCompiler(loader);
        try
        {
            return compiler.CompileStylesheet(node);
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new XsltException("The stylesheet is too deeply nested", ex);
        }
    }

    private sealed class Module
    {
        public required Element Root { get; init; }

        public string BaseUri { get; init; } = "";

        public bool ForwardsCompatible { get; init; }

        public List<(Element Element, Module Owner)> Declarations { get; } = [];

        public List<Module> Imports { get; } = [];

        public int Precedence { get; set; }

        public int ImportLow { get; set; }
    }

    /// <summary>The static context of an element of the stylesheet.</summary>
    private sealed record Scope
    {
        public required XsltNamespaceScope Namespaces { get; init; }

        public XsltVariableScope? Variables { get; init; }

        public bool PreserveSpace { get; init; }

        public required HashSet<string> ExcludedNamespaces { get; init; }

        public required HashSet<string> ExtensionNamespaces { get; init; }

        public bool ForwardsCompatible { get; init; }

        public required string BaseUri { get; init; }
    }

    private XsltStylesheet CompileStylesheet(Node node)
    {
        var root = LoadModule(node, [], depth: 0);

        // The import precedences (https://www.w3.org/TR/xslt-10/#import): an imported stylesheet has a lower precedence than
        // the importing one and than the stylesheets imported after it
        var modules = new List<Module>();
        AssignPrecedences(root, modules);

        // The declarations that the expressions and the literal result elements of the other declarations use
        foreach (var module in modules)
        {
            foreach (var (element, owner) in module.Declarations)
            {
                if (!IsXsl(element))
                    continue;

                var scope = CreateScope(element, owner);
                switch (element.LocalName)
                {
                    case "variable" or "param":
                        _compilation.GlobalNames.Add(ExpandQName(RequiredAttribute(element, "name"), scope.Namespaces));
                        break;
                    case "namespace-alias":
                        CompileNamespaceAlias(element, scope);
                        break;
                    case "decimal-format":
                        CompileDecimalFormat(element, scope);
                        break;
                    case "output":
                        CompileOutput(element, scope);
                        break;
                    case "strip-space" or "preserve-space":
                        CompileSpaceRules(element, scope, module.Precedence);
                        break;
                }
            }
        }

        foreach (var module in modules)
        {
            foreach (var (element, owner) in module.Declarations)
            {
                CompileDeclaration(element, owner, module);
            }
        }

        foreach (var rules in _stylesheet.TemplateRules.Values)
        {
            // The rule that applies is the first one: the highest import precedence, then the highest priority, then the last one
            rules.Sort((a, b) =>
            {
                var result = b.Template.Precedence.CompareTo(a.Template.Precedence);
                if (result == 0)
                {
                    result = b.Priority.CompareTo(a.Priority);
                }

                return result != 0 ? result : b.Position.CompareTo(a.Position);
            });
        }

        return _stylesheet;
    }

    private static void AssignPrecedences(Module module, List<Module> modules)
    {
        var low = modules.Count + 1;
        foreach (var import in module.Imports)
        {
            AssignPrecedences(import, modules);
        }

        module.ImportLow = low;
        module.Precedence = modules.Count + 1;
        modules.Add(module);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Modules: xsl:include and xsl:import (https://www.w3.org/TR/xslt-10/#section-Combining-Stylesheets)
    // ---------------------------------------------------------------------------------------------------------------

    private Module LoadModule(Node node, List<string> urls, int depth)
    {
        if (depth > MaxModuleDepth || ++_moduleCount > MaxModules)
            throw new XsltException("Too many included or imported stylesheets");

        var root = node switch
        {
            Element element => element,
            Document document => document.DocumentElement,
            _ => null,
        } ?? throw new XsltException("The stylesheet has no document element");

        var baseUri = root.BaseUri;
        if (node is Document && !_stylesheet.ModuleDocuments.ContainsKey(baseUri))
        {
            _stylesheet.ModuleDocuments[baseUri] = node;
        }

        if (IsXsl(root, "stylesheet") || IsXsl(root, "transform"))
        {
            var module = new Module { Root = root, BaseUri = baseUri, ForwardsCompatible = IsForwardsCompatible(root.GetAttributeValue("version")) };
            ExpandModule(root, module, module, urls, depth);
            return module;
        }

        // A literal result element as stylesheet (https://www.w3.org/TR/xslt-10/#result-element-stylesheet)
        if (root.GetAttributeValue(XsltNamespace, "version") is { } version)
        {
            var module = new Module { Root = root, BaseUri = baseUri, ForwardsCompatible = IsForwardsCompatible(version) };
            _simplifiedRoots.Add(root);
            module.Declarations.Add((root, module));
            return module;
        }

        throw new XsltException("The document is not an XSLT stylesheet");
    }

    private static bool IsForwardsCompatible(string? version) => version is not null && XsltValues.StringToNumber(version) != 1.0;

    private void ExpandModule(Element root, Module module, Module owner, List<string> urls, int depth)
    {
        for (var child = root.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (IsXsl(child, "import") || IsXsl(child, "include"))
            {
                var href = child.GetAttributeValue("href") ?? throw new XsltException($"xsl:{child.LocalName} requires an href attribute");
                var url = UrlParser.Parse(href, UrlParser.Parse(owner.BaseUri, baseUrl: null)) ?? throw new XsltException($"'{href}' is not a valid URL");
                url = url.Clone();
                url.Fragment = null;
                var serializedUrl = url.Serialize();
                if (urls.Contains(serializedUrl))
                    throw new XsltException($"The stylesheet '{serializedUrl}' includes or imports itself");

                var document = _loader?.Load(url, XsltLoadPurpose.Stylesheet) ?? throw new XsltException($"The stylesheet '{serializedUrl}' cannot be loaded");
                var nestedUrls = new List<string>(urls) { serializedUrl };
                var nested = LoadModule(document, nestedUrls, depth + 1);
                if (child.LocalName == "import")
                {
                    module.Imports.Add(nested);
                }
                else
                {
                    // The declarations of an included stylesheet replace the xsl:include, and its imports are imported
                    module.Declarations.AddRange(nested.Declarations);
                    module.Imports.AddRange(nested.Imports);
                }
            }
            else
            {
                module.Declarations.Add((child, owner));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Scopes
    // ---------------------------------------------------------------------------------------------------------------

    private XsltNamespaceScope GetNamespaceScope(Element element)
    {
        if (_namespaceScopes.TryGetValue(element, out var scope))
            return scope;

        // The ancestors whose scope is not computed yet, from the nearest
        var pending = new List<Element>();
        var parentScope = XsltNamespaceScope.Empty;
        for (Element? current = element; current is not null; current = current.ParentElement)
        {
            if (_namespaceScopes.TryGetValue(current, out var known))
            {
                parentScope = known;
                break;
            }

            pending.Add(current);
        }

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            parentScope = XsltNamespaceScope.Create(parentScope, pending[i]);
            _namespaceScopes[pending[i]] = parentScope;
        }

        return parentScope;
    }

    /// <summary>The scope of a top-level element: the exclusions and the extension namespaces of its stylesheet element.</summary>
    private Scope CreateScope(Element element, Module module)
    {
        var scope = new Scope
        {
            Namespaces = GetNamespaceScope(module.Root),
            ExcludedNamespaces = [XsltNamespace],
            ExtensionNamespaces = [],
            ForwardsCompatible = module.ForwardsCompatible,
            BaseUri = module.BaseUri,
        };

        var root = module.Root;
        if (!_simplifiedRoots.Contains(root))
        {
            AddPrefixes(scope.ExcludedNamespaces, root.GetAttributeValue("exclude-result-prefixes"), scope.Namespaces);
            AddPrefixes(scope.ExtensionNamespaces, root.GetAttributeValue("extension-element-prefixes"), scope.Namespaces);
            AddPrefixes(scope.ExcludedNamespaces, root.GetAttributeValue("extension-element-prefixes"), scope.Namespaces);
        }

        return ReferenceEquals(element, root) && !_simplifiedRoots.Contains(root) ? scope : Enter(scope, element);
    }

    /// <summary>The scope of an element from the scope of its parent.</summary>
    private Scope Enter(Scope parent, Element element)
    {
        var namespaces = GetNamespaceScope(element);
        var preserve = element.GetAttributeValue(Namespaces.Xml, "space") switch
        {
            "preserve" => true,
            "default" => false,
            _ => parent.PreserveSpace,
        };

        var forwardsCompatible = parent.ForwardsCompatible;
        var excluded = parent.ExcludedNamespaces;
        var extensions = parent.ExtensionNamespaces;
        if (!IsXsl(element))
        {
            if (element.GetAttributeValue(XsltNamespace, "version") is { } version)
            {
                forwardsCompatible = IsForwardsCompatible(version);
            }

            if (element.GetAttributeValue(XsltNamespace, "exclude-result-prefixes") is { } excludedPrefixes)
            {
                excluded = [.. excluded];
                AddPrefixes(excluded, excludedPrefixes, namespaces);
            }

            if (element.GetAttributeValue(XsltNamespace, "extension-element-prefixes") is { } extensionPrefixes)
            {
                excluded = [.. excluded];
                extensions = [.. extensions];
                AddPrefixes(excluded, extensionPrefixes, namespaces);
                AddPrefixes(extensions, extensionPrefixes, namespaces);
            }
        }

        return parent with
        {
            Namespaces = namespaces,
            PreserveSpace = preserve,
            ForwardsCompatible = forwardsCompatible,
            ExcludedNamespaces = excluded,
            ExtensionNamespaces = extensions,
        };
    }

    private static void AddPrefixes(HashSet<string> set, string? prefixes, XsltNamespaceScope namespaces)
    {
        if (prefixes is null)
            return;

        foreach (var prefix in prefixes.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ns = prefix == "#default" ? namespaces.Lookup("") : namespaces.Lookup(prefix);
            if (ns is null)
                throw new XsltException($"The namespace prefix '{prefix}' is not declared");

            set.Add(ns);
        }
    }

    private XsltExpressionContext CreateContext(Scope scope, XsltExpressionKind kind = XsltExpressionKind.Expression) => new(_compilation, scope.Namespaces, scope.Variables, scope.BaseUri, kind);

    private XsltExpression CompileExpression(string text, Scope scope, XsltExpressionKind kind = XsltExpressionKind.Expression) => XsltExpression.Compile(text, CreateContext(scope, kind));

    private XsltAvt CompileAvt(string text, Scope scope) => XsltAvt.Compile(text, CreateContext(scope));

    private XsltAvt? CompileOptionalAvt(Element element, string name, Scope scope) => element.GetAttributeValue(name) is { } value ? CompileAvt(value, scope) : null;

    // ---------------------------------------------------------------------------------------------------------------
    // Declarations
    // ---------------------------------------------------------------------------------------------------------------

    private void CompileDeclaration(Element element, Module owner, Module module)
    {
        var scope = CreateScope(element, owner);
        if (_simplifiedRoots.Contains(element))
        {
            // The literal result element is the template of the rule that matches the root node
            var match = XsltPattern.Compile("/", CreateContext(scope, XsltExpressionKind.PatternWithoutVariables));
            var template = new XsltTemplate { Match = match, Precedence = module.Precedence, ImportLow = module.ImportLow, Body = [CompileLiteralResultElement(element, scope)] };
            AddTemplateRules(template, priority: null);
            return;
        }

        if (!IsXsl(element))
            return;

        switch (element.LocalName)
        {
            case "template":
                CompileTemplate(element, scope, module);
                break;
            case "variable" or "param":
                CompileGlobalVariable(element, scope, module);
                break;
            case "key":
                CompileKey(element, scope);
                break;
            case "attribute-set":
                CompileAttributeSet(element, scope);
                break;
            case "namespace-alias" or "decimal-format" or "output" or "strip-space" or "preserve-space" or "import" or "include":
                break;
            default:
                if (!scope.ForwardsCompatible)
                    throw new XsltException($"xsl:{element.LocalName} is not allowed at the top level of a stylesheet");

                break;
        }
    }

    private void CompileTemplate(Element element, Scope scope, Module module)
    {
        var matchText = element.GetAttributeValue("match");
        var nameText = element.GetAttributeValue("name");
        if (matchText is null && nameText is null)
            throw new XsltException("xsl:template requires a match or a name attribute");

        double? priority = null;
        if (element.GetAttributeValue("priority") is { } priorityText)
        {
            var value = XsltValues.StringToNumber(priorityText);
            if (double.IsNaN(value))
                throw new XsltException($"'{priorityText}' is not a valid priority");

            priority = value;
        }

        var template = new XsltTemplate
        {
            Match = matchText is null ? null : XsltPattern.Compile(matchText, CreateContext(scope, XsltExpressionKind.PatternWithoutVariables)),
            Name = nameText is null ? null : ExpandQName(nameText, scope.Namespaces),
            Mode = element.GetAttributeValue("mode") is { } mode ? ExpandQName(mode, scope.Namespaces) : XsltStylesheet.DefaultMode,
            Precedence = module.Precedence,
            ImportLow = module.ImportLow,
        };

        var (parameters, body) = CompileTemplateContent(element, scope);
        template.Parameters = parameters;
        template.Body = body;
        if (template.Name is { } name)
        {
            if (_namedTemplatePrecedences.TryGetValue(name, out var existing))
            {
                if (existing == module.Precedence)
                    throw new XsltException($"The template '{name}' is defined twice");
            }

            if (!_namedTemplatePrecedences.TryGetValue(name, out existing) || existing < module.Precedence)
            {
                _namedTemplatePrecedences[name] = module.Precedence;
                _stylesheet.NamedTemplates[name] = template;
            }
        }

        if (template.Match is not null)
        {
            AddTemplateRules(template, priority);
        }
    }

    private void AddTemplateRules(XsltTemplate template, double? priority)
    {
        if (!_stylesheet.TemplateRules.TryGetValue(template.Mode, out var rules))
        {
            rules = [];
            _stylesheet.TemplateRules[template.Mode] = rules;
        }

        var position = ++_position;
        foreach (var alternative in template.Match!.Alternatives)
        {
            rules.Add(new XsltTemplateRule(template, alternative, priority ?? alternative.DefaultPriority, position));
        }
    }

    /// <summary>The leading xsl:param elements of a template, and the instructions that follow them.</summary>
    private (XsltParam[] Parameters, XsltInstruction[] Body) CompileTemplateContent(Element element, Scope scope)
    {
        var parameters = new List<XsltParam>();
        var variables = scope.Variables;
        Node? child = element.FirstChild;
        for (; child is not null; child = child.NextSibling)
        {
            if (child is Text text && IsXmlWhitespace(text.Data))
                continue;

            if (child is Element childElement && IsXsl(childElement, "param"))
            {
                var parameter = CompileVariableDefinition(childElement, Enter(scope with { Variables = variables }, childElement));
                if (parameters.Exists(p => p.Name == parameter.Name))
                    throw new XsltException($"The parameter '{parameter.Name}' is declared twice");

                parameters.Add(parameter);
                variables = new XsltVariableScope(parameter.Name, variables);
                continue;
            }

            if (child is Element or Text)
                break;
        }

        var body = CompileSequence(element, scope with { Variables = variables }, child);
        return ([.. parameters], body);
    }

    private void CompileGlobalVariable(Element element, Scope scope, Module module)
    {
        var definition = CompileVariableDefinition(element, scope);
        if (_stylesheet.GlobalVariables.TryGetValue(definition.Name, out var existing))
        {
            if (existing.Precedence == module.Precedence)
                throw new XsltException($"The global variable '{definition.Name}' is declared twice");

            if (existing.Precedence > module.Precedence)
                return;
        }

        _stylesheet.GlobalVariables[definition.Name] = new XsltGlobalVariable { Definition = definition, IsParameter = element.LocalName == "param", Precedence = module.Precedence };
    }

    private XsltParam CompileVariableDefinition(Element element, Scope scope)
    {
        var name = ExpandQName(RequiredAttribute(element, "name"), scope.Namespaces);
        var select = element.GetAttributeValue("select");
        return new XsltParam
        {
            Name = name,
            Select = select is null ? null : CompileExpression(select, scope),
            Content = select is null ? CompileSequence(element, scope) : [],
        };
    }

    private void CompileKey(Element element, Scope scope)
    {
        var name = ExpandQName(RequiredAttribute(element, "name"), scope.Namespaces);
        var match = XsltPattern.Compile(RequiredAttribute(element, "match"), CreateContext(scope, XsltExpressionKind.PatternWithoutVariables));
        var use = CompileExpression(RequiredAttribute(element, "use"), scope, XsltExpressionKind.KeyUse);
        if (!_stylesheet.Keys.TryGetValue(name, out var definitions))
        {
            definitions = [];
            _stylesheet.Keys[name] = definitions;
        }

        definitions.Add(new XsltKeyDefinition(match, use));
    }

    private void CompileAttributeSet(Element element, Scope scope)
    {
        var name = ExpandQName(RequiredAttribute(element, "name"), scope.Namespaces);
        if (!_stylesheet.AttributeSets.TryGetValue(name, out var set))
        {
            set = new XsltAttributeSet();
            _stylesheet.AttributeSets[name] = set;
        }

        set.UsedSets.AddRange(CompileUseAttributeSets(element.GetAttributeValue("use-attribute-sets"), scope));
        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (!IsXsl(child, "attribute"))
                throw new XsltException("xsl:attribute-set can only contain xsl:attribute elements");

            set.Attributes.Add(CompileInstruction(child, Enter(scope, child)));
        }
    }

    private XsltQName[] CompileUseAttributeSets(string? value, Scope scope)
    {
        if (value is null)
            return [];

        return [.. value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(name => ExpandQName(name, scope.Namespaces))];
    }

    private void CompileNamespaceAlias(Element element, Scope scope)
    {
        var stylesheetPrefix = RequiredAttribute(element, "stylesheet-prefix");
        var resultPrefix = RequiredAttribute(element, "result-prefix");
        var stylesheetNamespace = (stylesheetPrefix == "#default" ? scope.Namespaces.Lookup("") : scope.Namespaces.Lookup(stylesheetPrefix)) ?? (stylesheetPrefix == "#default" ? "" : throw new XsltException($"The namespace prefix '{stylesheetPrefix}' is not declared"));
        var resultNamespace = (resultPrefix == "#default" ? scope.Namespaces.Lookup("") : scope.Namespaces.Lookup(resultPrefix)) ?? (resultPrefix == "#default" ? "" : throw new XsltException($"The namespace prefix '{resultPrefix}' is not declared"));
        _namespaceAliases[stylesheetNamespace] = (resultPrefix == "#default" ? null : resultPrefix, resultNamespace.Length == 0 ? null : resultNamespace);
    }

    private void CompileDecimalFormat(Element element, Scope scope)
    {
        var name = element.GetAttributeValue("name") is { } nameText ? ExpandQName(nameText, scope.Namespaces) : XsltStylesheet.DefaultMode;
        var format = new XsltDecimalFormat();
        format = format with
        {
            DecimalSeparator = GetChar("decimal-separator", format.DecimalSeparator),
            GroupingSeparator = GetChar("grouping-separator", format.GroupingSeparator),
            Infinity = element.GetAttributeValue("infinity") ?? format.Infinity,
            MinusSign = GetChar("minus-sign", format.MinusSign),
            NaN = element.GetAttributeValue("NaN") ?? format.NaN,
            Percent = GetChar("percent", format.Percent),
            PerMille = GetChar("per-mille", format.PerMille),
            ZeroDigit = GetChar("zero-digit", format.ZeroDigit),
            Digit = GetChar("digit", format.Digit),
            PatternSeparator = GetChar("pattern-separator", format.PatternSeparator),
        };

        _stylesheet.DecimalFormats[name] = format;

        char GetChar(string attribute, char defaultValue)
        {
            var value = element.GetAttributeValue(attribute);
            if (value is null)
                return defaultValue;

            if (value.Length != 1)
                throw new XsltException($"The {attribute} attribute of xsl:decimal-format must be a single character");

            return value[0];
        }
    }

    private void CompileOutput(Element element, Scope scope)
    {
        var output = _stylesheet.Output;
        if (element.GetAttributeValue("method") is { } method)
        {
            method = method.Trim();
            output.Method = method is "xml" or "html" or "text" ? method : XsltValues.IsQName(method) && method.Contains(':', StringComparison.Ordinal) ? method : throw new XsltException($"'{method}' is not a valid output method");
        }

        output.Version = element.GetAttributeValue("version") ?? output.Version;
        output.Encoding = element.GetAttributeValue("encoding") ?? output.Encoding;
        output.OmitXmlDeclaration = element.GetAttributeValue("omit-xml-declaration") is { } omit ? omit == "yes" : output.OmitXmlDeclaration;
        output.Standalone = element.GetAttributeValue("standalone") ?? output.Standalone;
        output.DoctypePublic = element.GetAttributeValue("doctype-public") ?? output.DoctypePublic;
        output.DoctypeSystem = element.GetAttributeValue("doctype-system") ?? output.DoctypeSystem;
        output.Indent = element.GetAttributeValue("indent") is { } indent ? indent == "yes" : output.Indent;
        output.MediaType = element.GetAttributeValue("media-type") ?? output.MediaType;
        if (element.GetAttributeValue("cdata-section-elements") is { } cdataElements)
        {
            foreach (var name in cdataElements.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                output.CdataSectionElements.Add(ExpandQName(name, scope.Namespaces, useDefaultNamespace: true));
            }
        }
    }

    private void CompileSpaceRules(Element element, Scope scope, int precedence)
    {
        var strip = element.LocalName == "strip-space";
        foreach (var test in RequiredAttribute(element, "elements").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (test == "*")
            {
                _stylesheet.SpaceRules.Add(new XsltSpaceRule(strip, NamespaceUri: null, LocalName: null, precedence, -0.5));
            }
            else if (test.EndsWith(":*", StringComparison.Ordinal))
            {
                var prefix = test[..^2];
                var ns = XsltValues.IsNCName(prefix) ? scope.Namespaces.Lookup(prefix) : null;
                _stylesheet.SpaceRules.Add(new XsltSpaceRule(strip, ns ?? throw new XsltException($"'{test}' is not a valid name test"), LocalName: null, precedence, -0.25));
            }
            else
            {
                var name = ExpandQName(test, scope.Namespaces);
                _stylesheet.SpaceRules.Add(new XsltSpaceRule(strip, name.NamespaceUri, name.LocalName, precedence, 0));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Templates: instructions and literal result elements
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Compiles the children of an element (starting at <paramref name="first"/>) as a sequence of instructions.</summary>
    private XsltInstruction[] CompileSequence(Element parent, Scope scope, Node? first = null)
    {
        if (++_nestingDepth > MaxNestingDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new XsltException("The stylesheet is too deeply nested");

        try
        {
            var instructions = new List<XsltInstruction>();
            var variables = scope.Variables;
            for (var child = first ?? parent.FirstChild; child is not null; child = child.NextSibling)
            {
                switch (child)
                {
                    case Text text:
                        if (!scope.PreserveSpace && IsXmlWhitespace(text.Data))
                            break;

                        instructions.Add(new XsltTextInstruction(text.Data));
                        break;

                    case Element element:
                        var childScope = Enter(scope with { Variables = variables }, element);
                        if (IsXsl(element, "variable"))
                        {
                            var variable = CompileVariableDefinition(element, childScope);
                            instructions.Add(new XsltVariableInstruction(variable));
                            variables = new XsltVariableScope(variable.Name, variables);
                        }
                        else if (IsXsl(element, "fallback"))
                        {
                            // Only instantiated by an instruction that is not supported (https://www.w3.org/TR/xslt-10/#fallback)
                        }
                        else if (IsXsl(element))
                        {
                            instructions.Add(CompileInstruction(element, childScope));
                        }
                        else if (element.NamespaceUri is { } ns && childScope.ExtensionNamespaces.Contains(ns))
                        {
                            instructions.Add(new XsltFallbackInstruction(element.TagName, CompileFallbacks(element, childScope)));
                        }
                        else
                        {
                            instructions.Add(CompileLiteralResultElement(element, childScope));
                        }

                        break;
                }
            }

            return [.. instructions];
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private XsltInstruction[][] CompileFallbacks(Element element, Scope scope)
    {
        var fallbacks = new List<XsltInstruction[]>();
        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (IsXsl(child, "fallback"))
            {
                fallbacks.Add(CompileSequence(child, Enter(scope, child)));
            }
        }

        return [.. fallbacks];
    }

    private XsltLiteralResultElement CompileLiteralResultElement(Element element, Scope scope)
    {
        var (ns, prefix) = ApplyAlias(element.NamespaceUri, element.Prefix);
        var attributes = new List<XsltLiteralAttribute>();
        XsltQName[] useAttributeSets = [];
        for (var i = 0; i < element.AttributeCount; i++)
        {
            var attributeNamespace = element.GetAttributeNamespace(i);
            var localName = element.GetAttributeLocalName(i);
            if (attributeNamespace == XsltNamespace)
            {
                if (localName == "use-attribute-sets")
                {
                    useAttributeSets = CompileUseAttributeSets(element.GetAttributeValue(i), scope);
                }

                continue;
            }

            if (attributeNamespace == Namespaces.Xmlns)
                continue;

            var (attributeNs, attributePrefix) = attributeNamespace is null ? (null, null) : ApplyAlias(attributeNamespace, element.GetAttributePrefix(i));
            attributes.Add(new XsltLiteralAttribute(attributeNs, attributePrefix, localName, CompileAvt(element.GetAttributeValue(i), scope)));
        }

        // The namespace nodes, except the XSLT namespace, the excluded namespaces and the namespaces of the aliases
        // (https://www.w3.org/TR/xslt-10/#literal-result-element)
        var namespaces = new List<(string, string)>();
        foreach (var (namespacePrefix, namespaceUri) in scope.Namespaces.Namespaces)
        {
            if (scope.ExcludedNamespaces.Contains(namespaceUri))
                continue;

            if (_namespaceAliases.TryGetValue(namespaceUri, out var alias))
            {
                if (alias.NamespaceUri is { } aliasNamespace)
                {
                    namespaces.Add((alias.Prefix ?? "", aliasNamespace));
                }

                continue;
            }

            namespaces.Add((namespacePrefix, namespaceUri));
        }

        return new XsltLiteralResultElement
        {
            NamespaceUri = ns,
            Prefix = prefix,
            LocalName = element.LocalName,
            Namespaces = [.. namespaces],
            UseAttributeSets = useAttributeSets,
            Attributes = [.. attributes],
            Body = CompileSequence(element, scope),
        };
    }

    private (string? NamespaceUri, string? Prefix) ApplyAlias(string? ns, string? prefix)
    {
        if (_namespaceAliases.TryGetValue(ns ?? "", out var alias))
            return alias.NamespaceUri is null ? (null, null) : (alias.NamespaceUri, alias.Prefix);

        return (ns, prefix);
    }

    private XsltInstruction CompileInstruction(Element element, Scope scope)
    {
        switch (element.LocalName)
        {
            case "apply-templates":
            {
                var select = element.GetAttributeValue("select");
                var sorts = new List<XsltSortKey>();
                var parameters = new List<XsltParam>();
                for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
                {
                    if (IsXsl(child, "sort"))
                    {
                        sorts.Add(CompileSort(child, Enter(scope, child)));
                    }
                    else if (IsXsl(child, "with-param"))
                    {
                        parameters.Add(CompileVariableDefinition(child, Enter(scope, child)));
                    }
                    else
                    {
                        throw new XsltException("xsl:apply-templates can only contain xsl:sort and xsl:with-param elements");
                    }
                }

                return new XsltApplyTemplates
                {
                    Select = select is null ? null : CompileExpression(select, scope),
                    Mode = element.GetAttributeValue("mode") is { } mode ? ExpandQName(mode, scope.Namespaces) : XsltStylesheet.DefaultMode,
                    Sorts = [.. sorts],
                    Parameters = [.. parameters],
                };
            }

            case "call-template":
            {
                var parameters = new List<XsltParam>();
                for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
                {
                    if (!IsXsl(child, "with-param"))
                        throw new XsltException("xsl:call-template can only contain xsl:with-param elements");

                    parameters.Add(CompileVariableDefinition(child, Enter(scope, child)));
                }

                return new XsltCallTemplate(ExpandQName(RequiredAttribute(element, "name"), scope.Namespaces), [.. parameters]);
            }

            case "apply-imports":
                return new XsltApplyImports();

            case "for-each":
            {
                var sorts = new List<XsltSortKey>();
                Node? child = element.FirstChild;
                for (; child is not null; child = child.NextSibling)
                {
                    if (child is Text text && IsXmlWhitespace(text.Data))
                        continue;

                    if (child is not Element sortElement || !IsXsl(sortElement, "sort"))
                        break;

                    sorts.Add(CompileSort(sortElement, Enter(scope, sortElement)));
                }

                return new XsltForEach(CompileExpression(RequiredAttribute(element, "select"), scope), [.. sorts], CompileSequence(element, scope, child));
            }

            case "value-of":
                return new XsltValueOf(CompileExpression(RequiredAttribute(element, "select"), scope));

            case "copy-of":
                return new XsltCopyOf(CompileExpression(RequiredAttribute(element, "select"), scope));

            case "copy":
                return new XsltCopy(CompileUseAttributeSets(element.GetAttributeValue("use-attribute-sets"), scope), CompileSequence(element, scope));

            case "if":
                return new XsltIf(CompileExpression(RequiredAttribute(element, "test"), scope), CompileSequence(element, scope));

            case "choose":
            {
                var whens = new List<(XsltExpression, XsltInstruction[])>();
                XsltInstruction[]? otherwise = null;
                for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
                {
                    var childScope = Enter(scope, child);
                    if (IsXsl(child, "when") && otherwise is null)
                    {
                        whens.Add((CompileExpression(RequiredAttribute(child, "test"), childScope), CompileSequence(child, childScope)));
                    }
                    else if (IsXsl(child, "otherwise") && otherwise is null)
                    {
                        otherwise = CompileSequence(child, childScope);
                    }
                    else
                    {
                        throw new XsltException("xsl:choose can only contain xsl:when elements, then an xsl:otherwise element");
                    }
                }

                if (whens.Count == 0)
                    throw new XsltException("xsl:choose requires an xsl:when element");

                return new XsltChoose([.. whens], otherwise);
            }

            case "text":
            {
                var text = new System.Text.StringBuilder();
                for (var child = element.FirstChild; child is not null; child = child.NextSibling)
                {
                    if (child is Text textNode)
                    {
                        text.Append(textNode.Data);
                    }
                    else if (child is Element)
                    {
                        throw new XsltException("xsl:text can only contain text");
                    }
                }

                return new XsltTextInstruction(text.ToString());
            }

            case "element":
                return new XsltElementInstruction
                {
                    Name = CompileAvt(RequiredAttribute(element, "name"), scope),
                    Namespace = CompileOptionalAvt(element, "namespace", scope),
                    NamespaceScope = scope.Namespaces,
                    UseAttributeSets = CompileUseAttributeSets(element.GetAttributeValue("use-attribute-sets"), scope),
                    Body = CompileSequence(element, scope),
                };

            case "attribute":
                return new XsltAttributeInstruction
                {
                    Name = CompileAvt(RequiredAttribute(element, "name"), scope),
                    Namespace = CompileOptionalAvt(element, "namespace", scope),
                    NamespaceScope = scope.Namespaces,
                    Body = CompileSequence(element, scope),
                };

            case "comment":
                return new XsltCommentInstruction(CompileSequence(element, scope));

            case "processing-instruction":
                return new XsltProcessingInstructionInstruction(CompileAvt(RequiredAttribute(element, "name"), scope), CompileSequence(element, scope));

            case "number":
            {
                var level = element.GetAttributeValue("level") ?? "single";
                if (level is not ("single" or "multiple" or "any"))
                    throw new XsltException($"'{level}' is not a valid level of xsl:number");

                return new XsltNumberInstruction
                {
                    Level = level,
                    Count = element.GetAttributeValue("count") is { } count ? XsltPattern.Compile(count, CreateContext(scope, XsltExpressionKind.Pattern)) : null,
                    From = element.GetAttributeValue("from") is { } from ? XsltPattern.Compile(from, CreateContext(scope, XsltExpressionKind.Pattern)) : null,
                    Value = element.GetAttributeValue("value") is { } value ? CompileExpression(value, scope) : null,
                    Format = CompileOptionalAvt(element, "format", scope) ?? XsltAvt.Compile("1", CreateContext(scope)),
                    GroupingSeparator = CompileOptionalAvt(element, "grouping-separator", scope),
                    GroupingSize = CompileOptionalAvt(element, "grouping-size", scope),
                };
            }

            case "message":
                return new XsltMessage(element.GetAttributeValue("terminate") == "yes", CompileSequence(element, scope));

            default:
                // An unknown instruction is instantiated by its fallback in forwards-compatible mode (https://www.w3.org/TR/xslt-10/#forwards)
                if (scope.ForwardsCompatible && element.LocalName is not ("param" or "sort" or "with-param" or "when" or "otherwise" or "template" or "stylesheet" or "transform" or "import" or "include" or "key" or "output" or "attribute-set" or "decimal-format" or "namespace-alias" or "strip-space" or "preserve-space"))
                    return new XsltFallbackInstruction("xsl:" + element.LocalName, CompileFallbacks(element, scope));

                throw new XsltException($"xsl:{element.LocalName} is not allowed here");
        }
    }

    private XsltSortKey CompileSort(Element element, Scope scope)
    {
        return new XsltSortKey(
            CompileExpression(element.GetAttributeValue("select") ?? ".", scope),
            CompileOptionalAvt(element, "order", scope),
            CompileOptionalAvt(element, "data-type", scope),
            CompileOptionalAvt(element, "case-order", scope),
            CompileOptionalAvt(element, "lang", scope));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------------

    private static bool IsXsl(Element element) => element.NamespaceUri == XsltNamespace;

    private static bool IsXsl(Element element, string localName) => element.NamespaceUri == XsltNamespace && element.LocalName == localName;

    private static string RequiredAttribute(Element element, string name) => element.GetAttributeValue(name) ?? throw new XsltException($"xsl:{element.LocalName} requires a {name} attribute");

    private static bool IsXmlWhitespace(string text)
    {
        foreach (var c in text)
        {
            if (c is not (' ' or '\t' or '\r' or '\n'))
                return false;
        }

        return true;
    }

    /// <summary>Expands a QName (https://www.w3.org/TR/xslt-10/#qname): the default namespace is not used, except for the names of elements.</summary>
    private static XsltQName ExpandQName(string qname, XsltNamespaceScope namespaces, bool useDefaultNamespace = false)
    {
        qname = qname.Trim(' ', '\t', '\r', '\n');
        if (!XsltValues.IsQName(qname))
            throw new XsltException($"'{qname}' is not a QName");

        var colon = qname.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return new XsltQName(useDefaultNamespace ? namespaces.Lookup("") ?? "" : "", qname);

        var prefix = qname[..colon];
        var ns = namespaces.Lookup(prefix) ?? throw new XsltException($"The namespace prefix '{prefix}' is not declared");
        return new XsltQName(ns, qname[(colon + 1)..]);
    }
}
