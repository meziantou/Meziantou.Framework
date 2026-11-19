using System.Runtime.CompilerServices;
using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.XPath;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>The limits of a transformation, which stop the transformations that do not end or that are too large.</summary>
internal sealed record XsltLimits
{
    public static XsltLimits Default { get; } = new();

    /// <summary>The maximum nesting of the templates, as in libxslt (a recursion that does not end is an error, not a crash).</summary>
    public int MaxTemplateDepth { get; init; } = 3000;

    /// <summary>The maximum number of instructions and expressions evaluated.</summary>
    public long MaxSteps { get; init; } = long.MaxValue;

    /// <summary>The maximum number of nodes created in the result tree and in the result tree fragments.</summary>
    public long MaxOutputNodes { get; init; } = long.MaxValue;

    /// <summary>The maximum number of documents loaded by the document() function.</summary>
    public int MaxDocuments { get; init; } = 1000;
}

/// <summary>
/// A transformation (https://www.w3.org/TR/xslt-10/#section-Introduction): the processing of the nodes of a source tree by
/// the template rules of a stylesheet, which builds a result tree in a <see cref="DocumentFragment"/> of an XML document that
/// has no browsing context (see <see cref="XsltResultBuilder"/>).
/// </summary>
internal sealed class XsltExecution
{
    [ThreadStatic]
    private static XsltExecution? s_current;

    private readonly XsltStylesheet _stylesheet;
    private readonly IXsltDocumentLoader? _loader;
    private readonly bool _documentFunctionEnabled;
    private readonly XsltLimits _limits;
    private readonly IReadOnlyDictionary<XsltQName, object> _parameters;
    private readonly Dictionary<XsltQName, object> _globalValues = [];
    private readonly HashSet<XsltQName> _evaluatingGlobals = [];
    private readonly Dictionary<(XsltQName Name, Node Root), Dictionary<string, List<XPathNavigator>>> _keyIndexes = [];
    private readonly HashSet<XsltQName> _buildingKeys = [];
    private readonly Dictionary<string, XPathNavigator?> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> _generatedIds = [];
    private readonly HashSet<XsltQName> _usedAttributeSets = [];
    private readonly XsltWhitespaceStripper? _stripper;
    private XPathNavigator _globalContext = null!;
    private Binding? _locals;
    private XsltTemplate? _currentTemplate;
    private XsltQName _currentMode = XsltStylesheet.DefaultMode;
    private int _depth;
    private long _steps;

    private XsltExecution(XsltStylesheet stylesheet, IXsltDocumentLoader? loader, bool documentFunctionEnabled, IReadOnlyDictionary<XsltQName, object> parameters, XsltLimits limits)
    {
        _stylesheet = stylesheet;
        _loader = loader;
        _documentFunctionEnabled = documentFunctionEnabled;
        _parameters = parameters;
        _limits = limits;
        _stripper = stylesheet.HasStripSpace ? new XsltWhitespaceStripper(stylesheet) : null;
        ScratchDocument = new Document(isHtmlDocument: false, "application/xml");
        Output = new XsltResultBuilder(this, ScratchDocument.CreateDocumentFragment());
    }

    private sealed record Binding(XsltQName Name, object Value, Binding? Next);

    /// <summary>The document that owns the nodes of the result tree and of the result tree fragments.</summary>
    public Document ScratchDocument { get; }

    /// <summary>The result tree, or the result tree fragment, being built.</summary>
    public XsltResultBuilder Output { get; private set; }

    /// <summary>The current node (https://www.w3.org/TR/xslt-10/#section-Introduction).</summary>
    public XPathNavigator CurrentNode { get; private set; } = null!;

    /// <summary>The position of the current node in the current node list.</summary>
    public int Position { get; private set; } = 1;

    /// <summary>The size of the current node list.</summary>
    public int Size { get; private set; } = 1;

    /// <summary>The number of nodes created in the result trees.</summary>
    public long OutputNodeCount { get; set; }

    public static XsltExecution GetCurrent() => s_current ?? throw new InvalidOperationException("No XSLT transformation is running");

    public static XsltExecution? TryGetCurrent() => s_current;

    /// <summary>
    /// Transforms a source node: the template rules are applied to the node (https://www.w3.org/TR/xslt-10/#section-Processing-Model).
    /// Returns the fragment that contains the result tree.
    /// </summary>
    /// <param name="parameters">The values of the global parameters (strings, numbers, booleans and node-sets).</param>
    /// <param name="documentFunctionEnabled">Whether the document() function loads documents (it returns an empty node-set otherwise).</param>
    /// <exception cref="XsltException">A dynamic error of the transformation.</exception>
    public static DocumentFragment Transform(XsltStylesheet stylesheet, Node source, IReadOnlyDictionary<XsltQName, object> parameters, IXsltDocumentLoader? loader, bool documentFunctionEnabled, XsltLimits? limits = null)
    {
        var execution = new XsltExecution(stylesheet, loader, documentFunctionEnabled, parameters, limits ?? XsltLimits.Default);
        var previous = s_current;
        s_current = execution;
        try
        {
            return execution.Run(source);
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new XsltException("The transformation is too deeply nested", ex);
        }
        finally
        {
            s_current = previous;
        }
    }

    private DocumentFragment Run(Node source)
    {
        var node = new DomXPathNavigator(source, _stripper);
        var root = node.Clone();
        root.MoveToRoot();
        _globalContext = root;
        CurrentNode = node;
        ApplyTemplates(XsltNodeSet.FromSingle(node), XsltStylesheet.DefaultMode, parameters: null);
        return Output.Finish();
    }

    /// <summary>Counts a step, and stops the transformation when it exceeds its limits or the stack is too deep.</summary>
    public void CheckLimits()
    {
        if (++_steps > _limits.MaxSteps)
            throw new XsltException("The transformation exceeds the maximum number of steps");

        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new XsltException("The transformation is too deeply nested");
    }

    /// <summary>Counts a node of a result tree, and stops the transformation when there are too many.</summary>
    public void CountOutputNode()
    {
        if (++OutputNodeCount > _limits.MaxOutputNodes)
            throw new XsltException("The transformation creates too many nodes");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Expressions and variables
    // ---------------------------------------------------------------------------------------------------------------

    public object Evaluate(XsltExpression expression) => expression.Evaluate(CurrentNode, Position, Size);

    public XsltNodeSet EvaluateNodeSet(XsltExpression expression) => expression.EvaluateNodeSet(CurrentNode, Position, Size);

    public void BindLocal(XsltQName name, object value) => _locals = new Binding(name, value, _locals);

    /// <summary>The value of a variable or a parameter (https://www.w3.org/TR/xslt-10/#variable-values): its select expression, its content as a result tree fragment, or an empty string.</summary>
    public object EvaluateVariable(XsltParam variable)
    {
        if (variable.Select is not null)
            return Evaluate(variable.Select);

        if (variable.Content.Length == 0)
            return "";

        var fragment = ExecuteToFragment(variable.Content);
        return XsltNodeSet.FromResultTreeFragment(new DomXPathNavigator(fragment));
    }

    public object GetVariableValue(XsltQName name)
    {
        for (var binding = _locals; binding is not null; binding = binding.Next)
        {
            if (binding.Name == name)
                return binding.Value;
        }

        if (_globalValues.TryGetValue(name, out var value))
            return value;

        if (!_stylesheet.GlobalVariables.TryGetValue(name, out var global))
            throw new XsltException($"The variable '{name}' is not declared");

        if (!_evaluatingGlobals.Add(name))
            throw new XsltException($"The global variable '{name}' depends on itself");

        // A global variable is evaluated with the root node of the source tree as current node, when it is first used
        // (https://www.w3.org/TR/xslt-10/#top-level-variables)
        var state = SaveState();
        try
        {
            _locals = null;
            _currentTemplate = null;
            CurrentNode = _globalContext;
            Position = 1;
            Size = 1;
            value = global.IsParameter && _parameters.TryGetValue(name, out var parameter) ? parameter : EvaluateVariable(global.Definition);
        }
        finally
        {
            RestoreState(state);
            _evaluatingGlobals.Remove(name);
        }

        _globalValues[name] = value;
        return value;
    }

    public Dictionary<XsltQName, object>? EvaluateParameters(XsltParam[] parameters)
    {
        if (parameters.Length == 0)
            return null;

        var result = new Dictionary<XsltQName, object>();
        foreach (var parameter in parameters)
        {
            result[parameter.Name] = EvaluateVariable(parameter);
        }

        return result;
    }

    private readonly record struct State(XPathNavigator CurrentNode, int Position, int Size, Binding? Locals, XsltTemplate? Template, XsltQName Mode);

    private State SaveState() => new(CurrentNode, Position, Size, _locals, _currentTemplate, _currentMode);

    private void RestoreState(State state)
    {
        CurrentNode = state.CurrentNode;
        Position = state.Position;
        Size = state.Size;
        _locals = state.Locals;
        _currentTemplate = state.Template;
        _currentMode = state.Mode;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Instructions
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Instantiates a sequence of instructions; the variables it binds are only visible in the sequence.</summary>
    public void ExecuteSequence(XsltInstruction[] instructions)
    {
        var locals = _locals;
        try
        {
            foreach (var instruction in instructions)
            {
                CheckLimits();
                instruction.Execute(this);
            }
        }
        finally
        {
            _locals = locals;
        }
    }

    /// <summary>Instantiates instructions into a new result tree fragment.</summary>
    public DocumentFragment ExecuteToFragment(XsltInstruction[] instructions)
    {
        var output = Output;
        Output = new XsltResultBuilder(this, ScratchDocument.CreateDocumentFragment());
        try
        {
            ExecuteSequence(instructions);
            return Output.Finish();
        }
        finally
        {
            Output = output;
        }
    }

    /// <summary>
    /// Instantiates the content of an attribute, a comment, a processing instruction or a message: the text nodes it creates
    /// (the other nodes are ignored, https://www.w3.org/TR/xslt-10/#creating-attributes).
    /// </summary>
    public string ExecuteToText(XsltInstruction[] instructions)
    {
        if (instructions.Length == 0)
            return "";

        var fragment = ExecuteToFragment(instructions);
        var builder = new System.Text.StringBuilder();
        for (var child = fragment.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Text text)
            {
                builder.Append(text.Data);
            }
        }

        return builder.ToString();
    }

    /// <summary>The children of the current node (the default select of xsl:apply-templates).</summary>
    public XsltNodeSet SelectChildren()
    {
        var children = new List<XPathNavigator>();
        var child = CurrentNode.Clone();
        if (child.MoveToFirstChild())
        {
            do
            {
                children.Add(child.Clone());
            }
            while (child.MoveToNext());
        }

        return XsltNodeSet.FromSequence(children);
    }

    /// <summary>Applies the template rules of a mode to a list of nodes (https://www.w3.org/TR/xslt-10/#section-Applying-Template-Rules).</summary>
    public void ApplyTemplates(XsltNodeSet nodes, XsltQName mode, Dictionary<XsltQName, object>? parameters)
    {
        var state = SaveState();
        try
        {
            var count = nodes.Count;
            for (var i = 0; i < count; i++)
            {
                var node = nodes[i];
                CurrentNode = node;
                Position = i + 1;
                Size = count;
                var template = FindTemplate(node, mode, minPrecedence: int.MinValue, maxPrecedence: int.MaxValue);
                if (template is not null)
                {
                    InvokeTemplate(template, parameters, mode, isTemplateRule: true);
                }
                else
                {
                    ApplyBuiltInTemplate(node, mode);
                }
            }
        }
        finally
        {
            RestoreState(state);
        }
    }

    /// <summary>The built-in template rules (https://www.w3.org/TR/xslt-10/#built-in-rule).</summary>
    private void ApplyBuiltInTemplate(XPathNavigator node, XsltQName mode)
    {
        switch (node.NodeType)
        {
            case XPathNodeType.Root or XPathNodeType.Element:
                EnterTemplate();
                try
                {
                    ApplyTemplates(SelectChildren(), mode, parameters: null);
                }
                finally
                {
                    _depth--;
                }

                break;

            case XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace or XPathNodeType.Attribute:
                Output.Text(node.Value);
                break;
        }
    }

    private XsltTemplate? FindTemplate(XPathNavigator node, XsltQName mode, int minPrecedence, int maxPrecedence)
    {
        if (!_stylesheet.TemplateRules.TryGetValue(mode, out var rules))
            return null;

        foreach (var rule in rules)
        {
            var precedence = rule.Template.Precedence;
            if (precedence < minPrecedence || precedence > maxPrecedence)
                continue;

            if (rule.Alternative.Matches(node))
                return rule.Template;
        }

        return null;
    }

    private void EnterTemplate()
    {
        if (++_depth > _limits.MaxTemplateDepth)
        {
            _depth--;
            throw new XsltException("The templates are too deeply nested (an infinite recursion?)");
        }
    }

    private void InvokeTemplate(XsltTemplate template, Dictionary<XsltQName, object>? parameters, XsltQName mode, bool isTemplateRule)
    {
        EnterTemplate();
        var state = SaveState();
        try
        {
            _locals = null;
            if (isTemplateRule)
            {
                _currentTemplate = template;
                _currentMode = mode;
            }

            foreach (var parameter in template.Parameters)
            {
                var value = parameters is not null && parameters.TryGetValue(parameter.Name, out var passed) ? passed : EvaluateVariable(parameter);
                BindLocal(parameter.Name, value);
            }

            ExecuteSequence(template.Body);
        }
        finally
        {
            RestoreState(state);
            _depth--;
        }
    }

    public void CallTemplate(XsltQName name, Dictionary<XsltQName, object>? parameters)
    {
        if (!_stylesheet.NamedTemplates.TryGetValue(name, out var template))
            throw new XsltException($"The template '{name}' is not declared");

        InvokeTemplate(template, parameters, _currentMode, isTemplateRule: false);
    }

    /// <summary>xsl:apply-imports (https://www.w3.org/TR/xslt-10/#apply-imports): the template rules imported by the stylesheet of the current template rule.</summary>
    public void ApplyImports()
    {
        if (_currentTemplate is not { } current)
            throw new XsltException("xsl:apply-imports is used when there is no current template rule");

        var template = FindTemplate(CurrentNode, _currentMode, current.ImportLow, current.Precedence - 1);
        if (template is not null)
        {
            InvokeTemplate(template, parameters: null, _currentMode, isTemplateRule: true);
        }
        else
        {
            ApplyBuiltInTemplate(CurrentNode, _currentMode);
        }
    }

    /// <summary>xsl:for-each (https://www.w3.org/TR/xslt-10/#for-each): the current template rule is null in the body.</summary>
    public void ForEach(XsltNodeSet nodes, XsltInstruction[] body)
    {
        var state = SaveState();
        try
        {
            _currentTemplate = null;
            var count = nodes.Count;
            for (var i = 0; i < count; i++)
            {
                CurrentNode = nodes[i];
                Position = i + 1;
                Size = count;
                ExecuteSequence(body);
            }
        }
        finally
        {
            RestoreState(state);
        }
    }

    /// <summary>Adds the attributes of attribute sets to the element being created (https://www.w3.org/TR/xslt-10/#attribute-sets).</summary>
    public void UseAttributeSets(XsltQName[] names)
    {
        foreach (var name in names)
        {
            if (!_stylesheet.AttributeSets.TryGetValue(name, out var set))
                throw new XsltException($"The attribute set '{name}' is not declared");

            if (!_usedAttributeSets.Add(name))
                throw new XsltException($"The attribute set '{name}' uses itself");

            var locals = _locals;
            try
            {
                UseAttributeSets([.. set.UsedSets]);

                // The attribute sets only see the global variables
                _locals = null;
                foreach (var attribute in set.Attributes)
                {
                    attribute.Execute(this);
                }
            }
            finally
            {
                _locals = locals;
                _usedAttributeSets.Remove(name);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Sorting (https://www.w3.org/TR/xslt-10/#sorting)
    // ---------------------------------------------------------------------------------------------------------------

    public XsltNodeSet Sort(XsltNodeSet nodes, XsltSortKey[] sorts)
    {
        if (sorts.Length == 0 || nodes.Count <= 1)
            return nodes;

        var options = new (bool Descending, bool Number, bool UpperFirst)[sorts.Length];
        for (var k = 0; k < sorts.Length; k++)
        {
            var sort = sorts[k];
            var order = sort.Order?.Evaluate(this) ?? "ascending";
            var dataType = sort.DataType?.Evaluate(this) ?? "text";
            var caseOrder = sort.CaseOrder?.Evaluate(this) ?? "lower-first";
            options[k] = (order == "descending", dataType == "number", caseOrder == "upper-first");
        }

        // The sort keys are evaluated with each node as current node, in the unsorted node list
        var count = nodes.Count;
        var keys = new object[count][];
        var state = SaveState();
        try
        {
            for (var i = 0; i < count; i++)
            {
                CurrentNode = nodes[i];
                Position = i + 1;
                Size = count;
                var nodeKeys = new object[sorts.Length];
                for (var k = 0; k < sorts.Length; k++)
                {
                    var value = Evaluate(sorts[k].Select);
                    nodeKeys[k] = options[k].Number ? XsltValues.ToNumber(value) : XsltValues.ToStringValue(value);
                }

                keys[i] = nodeKeys;
            }
        }
        finally
        {
            RestoreState(state);
        }

        var indexes = Enumerable.Range(0, count).ToArray();
        var comparer = Comparer<int>.Create((a, b) =>
        {
            for (var k = 0; k < options.Length; k++)
            {
                var result = options[k].Number ? CompareNumbers((double)keys[a][k], (double)keys[b][k]) : CompareText((string)keys[a][k], (string)keys[b][k], options[k].UpperFirst);
                if (result != 0)
                    return options[k].Descending ? -result : result;
            }

            // A stable sort: the nodes keep their order in the node list
            return a.CompareTo(b);
        });

        Array.Sort(indexes, comparer);
        return XsltNodeSet.FromSequence([.. indexes.Select(index => nodes[index])]);

        static int CompareNumbers(double x, double y)
        {
            // NaN values precede the other numbers
            if (double.IsNaN(x))
                return double.IsNaN(y) ? 0 : -1;

            if (double.IsNaN(y))
                return 1;

            return x.CompareTo(y);
        }

        // The strings are compared by code point, ignoring the case first; the case order separates the strings that only
        // differ by case (the comparison does not depend on the culture)
        static int CompareText(string x, string y, bool upperFirst)
        {
            var result = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
                return result;

            result = string.CompareOrdinal(x, y);
            return upperFirst ? result : -result;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Functions (https://www.w3.org/TR/xslt-10/#add-func)
    // ---------------------------------------------------------------------------------------------------------------

    public object CallFunction(XsltFunction function, object[] args, XPathNavigator context)
    {
        switch (function.Kind)
        {
            case XsltFunctionKind.Current:
                return XsltNodeSet.FromSingle(CurrentNode);

            case XsltFunctionKind.Key:
                return Key(function.Context.ExpandQName(XsltValues.ToStringValue(args[0])), args[1], context);

            case XsltFunctionKind.Document:
                return Document(function, args);

            case XsltFunctionKind.FormatNumber:
            {
                var format = args.Length > 2
                    ? _stylesheet.DecimalFormats.GetValueOrDefault(function.Context.ExpandQName(XsltValues.ToStringValue(args[2]))) ?? throw new XsltException($"The decimal format '{XsltValues.ToStringValue(args[2])}' is not declared")
                    : _stylesheet.DecimalFormats.GetValueOrDefault(XsltStylesheet.DefaultMode) ?? new XsltDecimalFormat();
                return XsltNumbering.FormatNumber(XsltValues.ToNumber(args[0]), XsltValues.ToStringValue(args[1]), format);
            }

            case XsltFunctionKind.GenerateId:
            {
                XPathNavigator? node = context;
                if (args.Length > 0)
                {
                    node = args[0] is XsltNodeSet nodeSet ? (nodeSet.Count > 0 ? nodeSet[0] : null) : throw new XsltException("The argument of generate-id() must be a node-set");
                }

                return node is null ? "" : GenerateId(node);
            }

            case XsltFunctionKind.SystemProperty:
            {
                var name = function.Context.ExpandQName(XsltValues.ToStringValue(args[0]));
                if (name.NamespaceUri != XsltStylesheet.XsltNamespace)
                    return "";

                return name.LocalName switch
                {
                    "version" => 1.0,
                    "vendor" => "Meziantou.Framework.WebPlatform",
                    "vendor-url" => "https://github.com/meziantou/Meziantou.Framework",
                    _ => "",
                };
            }

            case XsltFunctionKind.ElementAvailable:
            {
                var name = function.Context.ExpandQName(XsltValues.ToStringValue(args[0]));
                return name.NamespaceUri == XsltStylesheet.XsltNamespace && name.LocalName is "apply-imports" or "apply-templates" or "attribute" or "call-template" or "choose" or "comment" or "copy" or "copy-of" or "element" or "fallback" or "for-each" or "if" or "message" or "number" or "processing-instruction" or "text" or "value-of" or "variable";
            }

            case XsltFunctionKind.FunctionAvailable:
                return function.Context.IsFunctionAvailable(XsltValues.ToStringValue(args[0]));

            case XsltFunctionKind.UnparsedEntityUri:
                return "";

            case XsltFunctionKind.NodeSet:
            {
                if (args[0] is XsltNodeSet nodeSet)
                    return nodeSet;

                // The node-set of a string is a text node (http://exslt.org/exsl/functions/node-set/)
                var fragment = ScratchDocument.CreateDocumentFragment();
                fragment.AppendChild(ScratchDocument.CreateTextNode(XsltValues.ToStringValue(args[0])));
                var text = new DomXPathNavigator(fragment);
                return text.MoveToFirstChild() ? XsltNodeSet.FromSingle(text) : XsltNodeSet.Empty;
            }

            case XsltFunctionKind.ObjectType:
                return XsltValues.GetObjectType(args[0]);

            default:
                throw new XsltException($"The function '{function.Name}' is not supported");
        }
    }

    private string GenerateId(XPathNavigator node)
    {
        object key = node.UnderlyingObject ?? (object)node.Clone();
        if (node.UnderlyingObject is null && node.NodeType == XPathNodeType.Namespace)
        {
            var parent = node.Clone();
            parent.MoveToParent();
            key = (parent.UnderlyingObject, node.LocalName);
        }

        if (!_generatedIds.TryGetValue(key, out var id))
        {
            id = "id" + (_generatedIds.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _generatedIds[key] = id;
        }

        return id;
    }

    /// <summary>The key() function (https://www.w3.org/TR/xslt-10/#function-key).</summary>
    private XsltNodeSet Key(XsltQName name, object value, XPathNavigator context)
    {
        var root = context.Clone();
        root.MoveToRoot();
        var index = GetKeyIndex(name, root);
        var result = new List<XPathNavigator>();
        if (value is XsltNodeSet nodeSet)
        {
            foreach (var node in nodeSet.Nodes)
            {
                if (index.TryGetValue(node.Value, out var nodes))
                {
                    result.AddRange(nodes);
                }
            }
        }
        else if (index.TryGetValue(XsltValues.ToStringValue(value), out var nodes))
        {
            result.AddRange(nodes);
        }

        return XsltNodeSet.FromUnordered(result);
    }

    private Dictionary<string, List<XPathNavigator>> GetKeyIndex(XsltQName name, XPathNavigator root)
    {
        if (!_stylesheet.Keys.TryGetValue(name, out var definitions))
            throw new XsltException($"The key '{name}' is not declared");

        var rootNode = root.UnderlyingObject as Node ?? throw new XsltException("The key() function cannot be used on this tree");
        if (_keyIndexes.TryGetValue((name, rootNode), out var index))
            return index;

        if (!_buildingKeys.Add(name))
            throw new XsltException($"The key '{name}' depends on itself");

        index = new Dictionary<string, List<XPathNavigator>>(StringComparer.Ordinal);
        var state = SaveState();
        try
        {
            _locals = null;

            // The nodes of the tree in document order: each node, its attributes, then its children
            var node = root.Clone();
            while (true)
            {
                IndexNode(node);
                if (node.NodeType == XPathNodeType.Element)
                {
                    var attribute = node.Clone();
                    if (attribute.MoveToFirstAttribute())
                    {
                        do
                        {
                            IndexNode(attribute);
                        }
                        while (attribute.MoveToNextAttribute());
                    }
                }

                if (node.MoveToFirstChild())
                    continue;

                while (!node.MoveToNext())
                {
                    if (!node.MoveToParent() || node.IsSamePosition(root))
                        goto Done;
                }
            }

        Done:
            _keyIndexes[(name, rootNode)] = index;
            return index;
        }
        finally
        {
            RestoreState(state);
            _buildingKeys.Remove(name);
        }

        void IndexNode(XPathNavigator candidate)
        {
            foreach (var definition in definitions)
            {
                if (!definition.Match.Matches(candidate))
                    continue;

                var current = candidate.Clone();
                CurrentNode = current;
                Position = 1;
                Size = 1;
                var value = definition.Use.Evaluate(current);
                if (value is XsltNodeSet values)
                {
                    foreach (var item in values.Nodes)
                    {
                        Add(item.Value, current);
                    }
                }
                else
                {
                    Add(XsltValues.ToStringValue(value), current);
                }
            }
        }

        void Add(string key, XPathNavigator node)
        {
            if (!index.TryGetValue(key, out var nodes))
            {
                nodes = [];
                index[key] = nodes;
            }

            if (nodes.Count == 0 || !nodes[^1].IsSamePosition(node))
            {
                nodes.Add(node);
            }
        }
    }

    /// <summary>The document() function (https://www.w3.org/TR/xslt-10/#function-document).</summary>
    private XsltNodeSet Document(XsltFunction function, object[] args)
    {
        if (!_documentFunctionEnabled)
            return XsltNodeSet.Empty;

        string? baseUri = null;
        if (args.Length > 1)
        {
            if (args[1] is not XsltNodeSet baseNodes)
                throw new XsltException("The second argument of document() must be a node-set");

            if (baseNodes.Count == 0)
                return XsltNodeSet.Empty;

            baseUri = baseNodes[0].BaseURI;
        }

        var result = new List<XPathNavigator>();
        if (args[0] is XsltNodeSet nodes)
        {
            foreach (var node in nodes.Nodes)
            {
                if (LoadDocument(node.Value, baseUri ?? node.BaseURI) is { } document)
                {
                    result.Add(document);
                }
            }
        }
        else if (LoadDocument(XsltValues.ToStringValue(args[0]), baseUri ?? function.Context.StaticBaseUri) is { } document)
        {
            result.Add(document);
        }

        return XsltNodeSet.FromUnordered(result);
    }

    private XPathNavigator? LoadDocument(string uri, string baseUri)
    {
        var baseUrl = UrlParser.Parse(baseUri, baseUrl: null);
        if (UrlParser.Parse(uri, baseUrl) is not { } url)
            return null;

        url = url.Clone();
        url.Fragment = null;
        var key = url.Serialize();
        if (_documents.TryGetValue(key, out var navigator))
            return navigator?.Clone();

        if (_documents.Count >= _limits.MaxDocuments)
            throw new XsltException("The transformation loads too many documents");

        // document("") is the stylesheet itself
        Node? document = _stylesheet.ModuleDocuments.GetValueOrDefault(key);
        document ??= _loader?.Load(url, XsltLoadPurpose.Document);
        navigator = document is null ? null : new DomXPathNavigator(document, _stripper);
        _documents[key] = navigator;
        return navigator?.Clone();
    }
}

/// <summary>Strips the whitespace text nodes of the source documents (https://www.w3.org/TR/xslt-10/#strip).</summary>
internal sealed class XsltWhitespaceStripper(XsltStylesheet stylesheet) : IXPathWhitespaceStripper
{
    private readonly Dictionary<(string Namespace, string LocalName), bool> _strippedElements = [];

    public bool IsStripped(Text firstOfRun)
    {
        for (Node? node = firstOfRun; node is Text text; node = node.NextSibling)
        {
            foreach (var c in text.Data)
            {
                if (c is not (' ' or '\t' or '\r' or '\n'))
                    return false;
            }
        }

        if (firstOfRun.ParentNode is not Element parent)
            return false;

        // The nearest xml:space attribute preserves the whitespace
        for (var element = parent; element is not null; element = element.ParentElement)
        {
            var space = element.GetAttributeValue(Namespaces.Xml, "space");
            if (space == "preserve")
                return false;

            if (space == "default")
                break;
        }

        var key = (parent.NamespaceUri ?? "", parent.LocalName);
        if (!_strippedElements.TryGetValue(key, out var stripped))
        {
            // The rule with the highest import precedence, then the highest priority, then the last one
            XsltSpaceRule? best = null;
            foreach (var rule in stylesheet.SpaceRules)
            {
                if (rule.Matches(key.Item1, key.LocalName) && (best is null || rule.Precedence > best.Precedence || (rule.Precedence == best.Precedence && rule.Priority >= best.Priority)))
                {
                    best = rule;
                }
            }

            stripped = best?.Strip == true;
            _strippedElements[key] = stripped;
        }

        return stripped;
    }
}
