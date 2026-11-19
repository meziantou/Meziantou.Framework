using System.Text;
using System.Xml.XPath;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>An instruction of a template (https://www.w3.org/TR/xslt-10/#section-Creating-the-Result-Tree), or a literal result element or text.</summary>
internal abstract class XsltInstruction
{
    public abstract void Execute(XsltExecution execution);
}

/// <summary>An attribute value template (https://www.w3.org/TR/xslt-10/#attribute-value-templates).</summary>
internal sealed class XsltAvt
{
    private readonly object[] _parts;

    private XsltAvt(object[] parts)
    {
        _parts = parts;
    }

    /// <summary>The value when the template has no expression.</summary>
    public string? ConstantValue => _parts is [] ? "" : _parts is [string text] ? text : null;

    public static XsltAvt Compile(string value, XsltExpressionContext context)
    {
        var parts = new List<object>();
        var literal = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '{')
            {
                if (i + 1 < value.Length && value[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                    continue;
                }

                // The expression ends at the first } that is not in a literal
                var end = i + 1;
                while (end < value.Length && value[end] != '}')
                {
                    if (value[end] is '"' or '\'')
                    {
                        var close = value.AsSpan(end + 1).IndexOf(value[end]);
                        if (close < 0)
                            throw new XsltException($"Unterminated literal in the attribute value template '{value}'");

                        end += close + 1;
                    }

                    end++;
                }

                if (end >= value.Length)
                    throw new XsltException($"Missing '}}' in the attribute value template '{value}'");

                if (literal.Length > 0)
                {
                    parts.Add(literal.ToString());
                    literal.Clear();
                }

                parts.Add(XsltExpression.Compile(value[(i + 1)..end], context));
                i = end;
            }
            else if (c == '}')
            {
                if (i + 1 < value.Length && value[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;
                    continue;
                }

                throw new XsltException($"'}}' must be doubled in the attribute value template '{value}'");
            }
            else
            {
                literal.Append(c);
            }
        }

        if (literal.Length > 0)
        {
            parts.Add(literal.ToString());
        }

        return new XsltAvt([.. parts]);
    }

    public string Evaluate(XsltExecution execution)
    {
        if (ConstantValue is { } constant)
            return constant;

        var builder = new StringBuilder();
        foreach (var part in _parts)
        {
            if (part is string text)
            {
                builder.Append(text);
            }
            else
            {
                builder.Append(XsltValues.ToStringValue(execution.Evaluate((XsltExpression)part)));
            }
        }

        return builder.ToString();
    }
}

/// <summary>Text of a template, or the content of xsl:text (https://www.w3.org/TR/xslt-10/#section-Creating-Text).</summary>
internal sealed class XsltTextInstruction(string text) : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.Output.Text(text);
}

/// <summary>An attribute of a literal result element.</summary>
internal sealed record XsltLiteralAttribute(string? NamespaceUri, string? Prefix, string LocalName, XsltAvt Value);

/// <summary>A literal result element (https://www.w3.org/TR/xslt-10/#literal-result-element).</summary>
internal sealed class XsltLiteralResultElement : XsltInstruction
{
    public required string? NamespaceUri { get; init; }

    public required string? Prefix { get; init; }

    public required string LocalName { get; init; }

    /// <summary>The namespace nodes copied to the result (prefix "" is the default namespace).</summary>
    public required (string Prefix, string NamespaceUri)[] Namespaces { get; init; }

    public required XsltQName[] UseAttributeSets { get; init; }

    public required XsltLiteralAttribute[] Attributes { get; init; }

    public required XsltInstruction[] Body { get; init; }

    public override void Execute(XsltExecution execution)
    {
        var output = execution.Output;
        output.StartElement(NamespaceUri, Prefix, LocalName);
        foreach (var (prefix, ns) in Namespaces)
        {
            output.NamespaceNode(prefix, ns);
        }

        execution.UseAttributeSets(UseAttributeSets);
        foreach (var attribute in Attributes)
        {
            output.Attribute(attribute.NamespaceUri, attribute.Prefix, attribute.LocalName, attribute.Value.Evaluate(execution));
        }

        execution.ExecuteSequence(Body);
        output.EndElement();
    }
}

/// <summary>xsl:value-of (https://www.w3.org/TR/xslt-10/#value-of).</summary>
internal sealed class XsltValueOf(XsltExpression select) : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.Output.Text(XsltValues.ToStringValue(execution.Evaluate(select)));
}

/// <summary>A sort key of xsl:apply-templates or xsl:for-each (https://www.w3.org/TR/xslt-10/#sorting).</summary>
internal sealed record XsltSortKey(XsltExpression Select, XsltAvt? Order, XsltAvt? DataType, XsltAvt? CaseOrder, XsltAvt? Lang);

/// <summary>xsl:apply-templates (https://www.w3.org/TR/xslt-10/#section-Applying-Template-Rules).</summary>
internal sealed class XsltApplyTemplates : XsltInstruction
{
    public required XsltExpression? Select { get; init; }

    public required XsltQName Mode { get; init; }

    public required XsltSortKey[] Sorts { get; init; }

    public required XsltParam[] Parameters { get; init; }

    public override void Execute(XsltExecution execution)
    {
        var nodes = Select is null ? execution.SelectChildren() : execution.EvaluateNodeSet(Select);
        nodes = execution.Sort(nodes, Sorts);
        var parameters = execution.EvaluateParameters(Parameters);
        execution.ApplyTemplates(nodes, Mode, parameters);
    }
}

/// <summary>xsl:call-template (https://www.w3.org/TR/xslt-10/#named-templates).</summary>
internal sealed class XsltCallTemplate(XsltQName name, XsltParam[] parameters) : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.CallTemplate(name, execution.EvaluateParameters(parameters));
}

/// <summary>xsl:apply-imports (https://www.w3.org/TR/xslt-10/#apply-imports).</summary>
internal sealed class XsltApplyImports : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.ApplyImports();
}

/// <summary>xsl:for-each (https://www.w3.org/TR/xslt-10/#for-each).</summary>
internal sealed class XsltForEach(XsltExpression select, XsltSortKey[] sorts, XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        var nodes = execution.Sort(execution.EvaluateNodeSet(select), sorts);
        execution.ForEach(nodes, body);
    }
}

/// <summary>xsl:if (https://www.w3.org/TR/xslt-10/#section-Conditional-Processing-with-xsl:if).</summary>
internal sealed class XsltIf(XsltExpression test, XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        if (XsltValues.ToBoolean(execution.Evaluate(test)))
        {
            execution.ExecuteSequence(body);
        }
    }
}

/// <summary>xsl:choose (https://www.w3.org/TR/xslt-10/#section-Conditional-Processing-with-xsl:choose).</summary>
internal sealed class XsltChoose((XsltExpression Test, XsltInstruction[] Body)[] whens, XsltInstruction[]? otherwise) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        foreach (var (test, body) in whens)
        {
            if (XsltValues.ToBoolean(execution.Evaluate(test)))
            {
                execution.ExecuteSequence(body);
                return;
            }
        }

        if (otherwise is not null)
        {
            execution.ExecuteSequence(otherwise);
        }
    }
}

/// <summary>A local xsl:variable (https://www.w3.org/TR/xslt-10/#local-variables): binds the variable for the following siblings.</summary>
internal sealed class XsltVariableInstruction(XsltParam variable) : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.BindLocal(variable.Name, execution.EvaluateVariable(variable));
}

/// <summary>xsl:copy-of (https://www.w3.org/TR/xslt-10/#copy-of).</summary>
internal sealed class XsltCopyOf(XsltExpression select) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        var value = execution.Evaluate(select);
        if (value is XsltNodeSet nodeSet)
        {
            foreach (var node in nodeSet.Nodes)
            {
                execution.Output.CopyNode(node, deep: true);
            }
        }
        else
        {
            execution.Output.Text(XsltValues.ToStringValue(value));
        }
    }
}

/// <summary>xsl:copy (https://www.w3.org/TR/xslt-10/#copying).</summary>
internal sealed class XsltCopy(XsltQName[] useAttributeSets, XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        var node = execution.CurrentNode;
        switch (node.NodeType)
        {
            case XPathNodeType.Root:
                execution.ExecuteSequence(body);
                break;

            case XPathNodeType.Element:
                execution.Output.CopyNode(node, deep: false);
                execution.UseAttributeSets(useAttributeSets);
                execution.ExecuteSequence(body);
                execution.Output.EndElement();
                break;

            default:
                execution.Output.CopyNode(node, deep: false);
                break;
        }
    }
}

/// <summary>xsl:element (https://www.w3.org/TR/xslt-10/#section-Creating-Elements-with-xsl:element).</summary>
internal sealed class XsltElementInstruction : XsltInstruction
{
    public required XsltAvt Name { get; init; }

    public required XsltAvt? Namespace { get; init; }

    public required XsltNamespaceScope NamespaceScope { get; init; }

    public required XsltQName[] UseAttributeSets { get; init; }

    public required XsltInstruction[] Body { get; init; }

    public override void Execute(XsltExecution execution)
    {
        var name = Name.Evaluate(execution);
        if (!XsltValues.IsQName(name))
        {
            // Recovery (https://www.w3.org/TR/xslt-10/#section-Creating-Elements-with-xsl:element): the content is instantiated without the element
            execution.ExecuteSequence(Body);
            return;
        }

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        var prefix = colon < 0 ? null : name[..colon];
        var localName = colon < 0 ? name : name[(colon + 1)..];
        string? ns;
        if (Namespace is not null)
        {
            ns = Namespace.Evaluate(execution);
        }
        else
        {
            ns = NamespaceScope.Lookup(prefix ?? "");
            if (ns is null && prefix is not null)
            {
                execution.ExecuteSequence(Body);
                return;
            }
        }

        if (string.IsNullOrEmpty(ns))
        {
            ns = null;
            prefix = null;
        }

        execution.Output.StartElement(ns, prefix, localName);
        execution.UseAttributeSets(UseAttributeSets);
        execution.ExecuteSequence(Body);
        execution.Output.EndElement();
    }
}

/// <summary>xsl:attribute (https://www.w3.org/TR/xslt-10/#creating-attributes).</summary>
internal sealed class XsltAttributeInstruction : XsltInstruction
{
    public required XsltAvt Name { get; init; }

    public required XsltAvt? Namespace { get; init; }

    public required XsltNamespaceScope NamespaceScope { get; init; }

    public required XsltInstruction[] Body { get; init; }

    public override void Execute(XsltExecution execution)
    {
        var name = Name.Evaluate(execution);
        var value = execution.ExecuteToText(Body);
        if (!XsltValues.IsQName(name) || name == "xmlns")
            return;

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        var prefix = colon < 0 ? null : name[..colon];
        var localName = colon < 0 ? name : name[(colon + 1)..];
        string? ns;
        if (Namespace is not null)
        {
            ns = Namespace.Evaluate(execution);
        }
        else if (prefix is not null)
        {
            ns = NamespaceScope.Lookup(prefix);
            if (ns is null)
                return;
        }
        else
        {
            ns = null;
        }

        if (string.IsNullOrEmpty(ns))
        {
            ns = null;
            prefix = null;
        }
        else if (prefix is "xmlns")
        {
            prefix = null;
        }

        execution.Output.Attribute(ns, prefix, localName, value);
    }
}

/// <summary>xsl:comment (https://www.w3.org/TR/xslt-10/#section-Creating-Comments).</summary>
internal sealed class XsltCommentInstruction(XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution) => execution.Output.Comment(execution.ExecuteToText(body));
}

/// <summary>xsl:processing-instruction (https://www.w3.org/TR/xslt-10/#section-Creating-Processing-Instructions).</summary>
internal sealed class XsltProcessingInstructionInstruction(XsltAvt name, XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        var target = name.Evaluate(execution);
        var data = execution.ExecuteToText(body);
        if (!XsltValues.IsNCName(target) || string.Equals(target, "xml", StringComparison.OrdinalIgnoreCase))
            return;

        execution.Output.ProcessingInstruction(target, data);
    }
}

/// <summary>xsl:number (https://www.w3.org/TR/xslt-10/#number).</summary>
internal sealed class XsltNumberInstruction : XsltInstruction
{
    public required string Level { get; init; }

    public required XsltPattern? Count { get; init; }

    public required XsltPattern? From { get; init; }

    public required XsltExpression? Value { get; init; }

    public required XsltAvt Format { get; init; }

    public required XsltAvt? GroupingSeparator { get; init; }

    public required XsltAvt? GroupingSize { get; init; }

    public override void Execute(XsltExecution execution)
    {
        List<double> numbers;
        if (Value is not null)
        {
            var value = XsltValues.ToNumber(execution.Evaluate(Value));
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.5)
            {
                execution.Output.Text(XsltValues.NumberToString(value));
                return;
            }

            numbers = [Math.Floor(value + 0.5)];
        }
        else
        {
            numbers = XsltNumbering.Count(execution.CurrentNode, Level, Count, From);
        }

        var groupingSeparator = GroupingSeparator?.Evaluate(execution);
        var groupingSize = 0;
        if (GroupingSize is not null && XsltValues.StringToNumber(GroupingSize.Evaluate(execution)) is var size && !double.IsNaN(size))
        {
            groupingSize = (int)Math.Clamp(size, 0, 1000);
        }

        execution.Output.Text(XsltNumbering.Format(numbers, Format.Evaluate(execution), groupingSeparator is { Length: > 0 } ? groupingSeparator : null, groupingSize));
    }
}

/// <summary>xsl:message (https://www.w3.org/TR/xslt-10/#message): the message is not reported; terminate="yes" stops the transformation.</summary>
internal sealed class XsltMessage(bool terminate, XsltInstruction[] body) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        var message = execution.ExecuteToText(body);
        if (terminate)
            throw new XsltException("The transformation was terminated by xsl:message: " + message);
    }
}

/// <summary>
/// An instruction that is not supported (an extension element, or an unknown XSLT element in forwards-compatible mode): its
/// xsl:fallback children are instantiated (https://www.w3.org/TR/xslt-10/#fallback), or it is an error.
/// </summary>
internal sealed class XsltFallbackInstruction(string name, XsltInstruction[][] fallbacks) : XsltInstruction
{
    public override void Execute(XsltExecution execution)
    {
        if (fallbacks.Length == 0)
            throw new XsltException($"The instruction '{name}' is not supported");

        foreach (var fallback in fallbacks)
        {
            execution.ExecuteSequence(fallback);
        }
    }
}
