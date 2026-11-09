using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>Reads the MSBuild properties of a project file, and the property references of its values.</summary>
internal static class MsBuildProperties
{
    private static readonly XName ConditionXName = XName.Get("Condition");

    /// <summary>Determines whether a value contains a property, item or metadata reference.</summary>
    public static bool IsExpression(string value)
    {
        return value.Contains("$(", StringComparison.Ordinal)
            || value.Contains("@(", StringComparison.Ordinal)
            || value.Contains("%(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the references of a value. A property reference, <c>$(Name)</c>, has a name. Any other expression, such as a
    /// property function (<c>$([System.DateTime]::Now)</c>), an item list (<c>@(Items)</c>) or a metadata (<c>%(Name)</c>), has none.
    /// </summary>
    public static List<VariableReference> GetReferences(string value)
    {
        var result = new List<VariableReference>();
        var index = 0;
        while (index < value.Length - 1)
        {
            if (value[index] is not ('$' or '@' or '%') || value[index + 1] is not '(')
            {
                index++;
                continue;
            }

            var end = FindClosingParenthesis(value, index + 1);
            if (end < 0)
            {
                result.Add(new VariableReference(index, value.Length - index, Name: null));
                break;
            }

            var name = value.AsSpan(index + 2, end - index - 2).Trim();
            var isProperty = value[index] is '$' && IsPropertyName(name);
            result.Add(new VariableReference(index, end + 1 - index, isProperty ? name.ToString() : null));
            index = end + 1;
        }

        return result;
    }

    /// <summary>
    /// Gets the properties of a project that a value can be resolved from. A property is only resolved when the project
    /// defines it once, in a <c>PropertyGroup</c> of the project without condition, with a literal value.
    /// </summary>
    /// <remarks>A property defined in a target, under a <c>Choose</c> element, or with a condition, may have another value, so it is not resolved.</remarks>
    public static VariableDefinitions Read(ScanFileContext context, XElement root)
    {
        // MSBuild property names are case-insensitive
        var result = new VariableDefinitions(StringComparer.OrdinalIgnoreCase);
        var ns = root.Name.Namespace;
        foreach (var propertyGroup in root.Descendants(ns + "PropertyGroup"))
        {
            var isUnconditional = propertyGroup.Parent == root && propertyGroup.Attribute(ConditionXName) is null;
            foreach (var element in propertyGroup.Elements())
            {
                if (element.Name.Namespace != ns)
                    continue;

                var value = isUnconditional && element.Attribute(ConditionXName) is null ? GetLiteralValue(element) : null;
                result.Add(element.Name.LocalName, value, () => CreateLocation(context, element));
            }
        }

        return result;
    }

    private static string? GetLiteralValue(XElement element)
    {
        if (element.HasElements)
            return null;

        var value = element.Value.Trim();
        if (value.Length is 0 || IsExpression(value))
            return null;

        return value;
    }

    private static Location CreateLocation(ScanFileContext context, XElement element)
    {
        // The value of an element holding a comment or a CDATA section does not map onto a single run of text
        if (element.Nodes().Any(node => node.NodeType is not XmlNodeType.Text))
            return new NonUpdatableLocation(context);

        var value = element.Value;
        var start = value.Length - value.AsSpan().TrimStart().Length;
        var length = value.AsSpan().Trim().Length;
        if (start is 0 && length == value.Length)
            return new XmlLocation(context.FileSystem, context.FullPath, element);

        return new XmlLocation(context.FileSystem, context.FullPath, element, start, length);
    }

    private static int FindClosingParenthesis(string value, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] is '(')
            {
                depth++;
            }
            else if (value[i] is ')')
            {
                depth--;
                if (depth is 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool IsPropertyName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || !(char.IsAsciiLetter(name[0]) || name[0] is '_'))
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
                return false;
        }

        return true;
    }
}
