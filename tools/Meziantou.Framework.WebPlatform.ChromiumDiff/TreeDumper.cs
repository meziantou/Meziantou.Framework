using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace ChromiumDiff;

/// <summary>Serializes the children of a node in the html5lib tree format, with the shadow roots (the same output as tree.js).</summary>
internal static class TreeDumper
{
    public static string Dump(Node root)
    {
        var lines = new List<string>();
        DumpChildren(lines, root, 0);
        return string.Join('\n', lines);
    }

    private static string Indent(int depth) => "| " + new string(' ', depth * 2);

    private static void DumpChildren(List<string> lines, Node parent, int depth)
    {
        for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            Dump(lines, child, depth);
        }
    }

    private static void Dump(List<string> lines, Node node, int depth)
    {
        switch (node)
        {
            case DocumentType doctype:
                lines.Add(Indent(depth) + "<!DOCTYPE " + doctype.Name + (doctype.PublicId.Length > 0 || doctype.SystemId.Length > 0 ? " \"" + doctype.PublicId + "\" \"" + doctype.SystemId + "\"" : "") + ">");
                break;

            case Element element:
            {
                var prefix = element.NamespaceUri switch
                {
                    "http://www.w3.org/2000/svg" => "svg ",
                    "http://www.w3.org/1998/Math/MathML" => "math ",
                    _ => "",
                };
                lines.Add(Indent(depth) + "<" + prefix + element.LocalName + ">");

                var attributes = new List<(string Name, string Value)>();
                foreach (var attribute in element.Attributes)
                {
                    var attributePrefix = attribute.NamespaceUri switch
                    {
                        "http://www.w3.org/1999/xlink" => "xlink ",
                        "http://www.w3.org/XML/1998/namespace" => "xml ",
                        "http://www.w3.org/2000/xmlns/" => "xmlns ",
                        _ => "",
                    };
                    attributes.Add((attributePrefix + attribute.LocalName, attribute.Value));
                }

                foreach (var (name, value) in attributes.OrderBy(a => a.Name, StringComparer.Ordinal))
                {
                    lines.Add(Indent(depth + 1) + name + "=\"" + value + "\"");
                }

                if (element.ShadowRoot is { } shadowRoot)
                {
                    lines.Add(Indent(depth + 1) + "#shadow-root (" + (shadowRoot.Mode == ShadowRootMode.Open ? "open" : "closed") + ")");
                    DumpChildren(lines, shadowRoot, depth + 2);
                }

                if (element is HtmlTemplateElement template)
                {
                    lines.Add(Indent(depth + 1) + "content");
                    DumpChildren(lines, template.Content, depth + 2);
                }

                DumpChildren(lines, element, depth + 1);
                break;
            }

            case Text text:
                lines.Add(Indent(depth) + "\"" + text.Data + "\"");
                break;

            case Comment comment:
                lines.Add(Indent(depth) + "<!-- " + comment.Data + " -->");
                break;

            case ProcessingInstruction pi:
                lines.Add(Indent(depth) + "<?" + pi.Target + " " + pi.Data + "?>");
                break;
        }
    }

    /// <summary>Returns the first different line of two dumps, with a few lines of context.</summary>
    public static string Diff(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var index = 0;
        while (index < expectedLines.Length && index < actualLines.Length && expectedLines[index] == actualLines[index])
        {
            index++;
        }

        var result = new StringBuilder();
        var start = Math.Max(0, index - 3);
        result.Append("  first difference at line ").Append(index + 1).Append('\n');
        result.Append("  chromium:\n");
        foreach (var line in expectedLines.Skip(start).Take(index - start + 4))
        {
            result.Append("    ").Append(line).Append('\n');
        }

        result.Append("  webplatform:\n");
        foreach (var line in actualLines.Skip(start).Take(index - start + 4))
        {
            result.Append("    ").Append(line).Append('\n');
        }

        return result.ToString();
    }
}
