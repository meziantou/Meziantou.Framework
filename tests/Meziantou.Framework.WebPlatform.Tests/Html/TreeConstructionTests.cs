using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// Tree construction tests (html5lib format), maintained in web-platform-tests/html/syntax/parsing/resources.
/// </summary>
public sealed class TreeConstructionTests
{
    public sealed record TreeTest(string File, int Index, string? Description, string Data, string? FragmentContext, bool? ScriptingEnabled, string ExpectedTree, IReadOnlyList<string> ExpectedErrors);

    private static readonly Lazy<Dictionary<string, List<TreeTest>>> TestCases = new(LoadTests);

    private static Dictionary<string, List<TreeTest>> LoadTests()
    {
        var result = new Dictionary<string, List<TreeTest>>(StringComparer.Ordinal);
        var directory = TestPaths.External("wpt", "html", "syntax", "parsing", "resources");
        foreach (var file in Directory.GetFiles(directory, "*.dat").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            result[name] = ParseFile(name, File.ReadAllText(file));
        }

        return result;
    }

    private static List<TreeTest> ParseFile(string file, string content)
    {
        var tests = new List<TreeTest>();
        var lines = content.Split('\n');
        var i = 0;
        var index = 0;
        while (i < lines.Length)
        {
            // A test may begin with a description (the lines until #data)
            string? description = null;
            if (lines[i] == "#description")
            {
                i++;
                var descriptionLines = new List<string>();
                while (i < lines.Length && lines[i] != "#data")
                {
                    descriptionLines.Add(lines[i]);
                    i++;
                }

                description = string.Join('\n', descriptionLines);
            }

            if (i >= lines.Length || lines[i] != "#data")
            {
                i++;
                continue;
            }

            i++;
            var data = new List<string>();
            while (i < lines.Length && lines[i] != "#errors")
            {
                data.Add(lines[i]);
                i++;
            }

            string? fragment = null;
            bool? scripting = null;
            var tree = new StringBuilder();
            var errors = new List<string>();
            while (i < lines.Length && lines[i] != "#document")
            {
                if (lines[i] is not ("#errors" or "#new-errors" or "#document-fragment" or "#script-on" or "#script-off") && lines[i].Length > 0)
                {
                    // Each line of #errors and #new-errors is a parse error (only their number matters)
                    errors.Add(lines[i]);
                }
                else if (lines[i] == "#document-fragment")
                {
                    fragment = lines[i + 1];
                    i++;
                }
                else if (lines[i] == "#script-on")
                {
                    scripting = true;
                }
                else if (lines[i] == "#script-off")
                {
                    scripting = false;
                }

                i++;
            }

            i++;
            var treeLines = new List<string>();
            while (i < lines.Length && lines[i] is not ("#data" or "#description"))
            {
                treeLines.Add(lines[i]);
                i++;
            }

            // Tests are separated by an empty line (text nodes can contain empty lines, so only remove one separator)
            if (treeLines.Count > 0 && treeLines[^1].Length == 0)
            {
                treeLines.RemoveAt(treeLines.Count - 1);
            }

            if (i >= lines.Length)
            {
                while (treeLines.Count > 0 && treeLines[^1].Length == 0)
                {
                    treeLines.RemoveAt(treeLines.Count - 1);
                }
            }

            foreach (var line in treeLines)
            {
                tree.Append(line).Append('\n');
            }

            tests.Add(new TreeTest(file, index++, description, string.Join('\n', data), fragment, scripting, tree.ToString(), errors));
        }

        return tests;
    }

    public static TheoryData<string, int, bool> Tests()
    {
        var data = new TheoryData<string, int, bool>();
        foreach (var (file, tests) in TestCases.Value.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            // Scripted tests require script execution (document.write)
            if (file.StartsWith("scripted_", StringComparison.Ordinal))
                continue;

            foreach (var test in tests)
            {
                if (test.ScriptingEnabled is null or true)
                {
                    data.Add(file, test.Index, true);
                }

                if (test.ScriptingEnabled is null or false)
                {
                    data.Add(file, test.Index, false);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Tests))]
    public void Parse(string file, int index, bool scripting)
    {
        var test = TestCases.Value[file][index];
        if (test.Description is not null)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Description: " + test.Description);
        }

        TestContext.Current.TestOutputHelper?.WriteLine("Input:\n" + test.Data);
        TestContext.Current.TestOutputHelper?.WriteLine("Context: " + test.FragmentContext);

        var errors = new List<HtmlParseError>();
        var options = new HtmlParserOptions { ScriptingEnabled = scripting, ParseErrorHandler = errors.Add };
        string actual;
        if (test.FragmentContext is null)
        {
            var document = new HtmlDocumentParser(options).Parse(test.Data);
            actual = TreeDumper.Dump(document);
        }
        else
        {
            var contextDocument = new HtmlDocumentParser(new HtmlParserOptions { ScriptingEnabled = scripting }).Parse("");
            var parts = test.FragmentContext.Split(' ');
            var context = parts.Length == 1
                ? contextDocument.CreateElement(parts[0])
                : contextDocument.CreateElementNS(parts[0] == "svg" ? "http://www.w3.org/2000/svg" : "http://www.w3.org/1998/Math/MathML", parts[1]);
            var fragment = new HtmlDocumentParser(options).ParseFragment(test.Data, context);
            actual = TreeDumper.Dump(fragment);
        }

        Assert.Equal(test.ExpectedTree, actual);

        // The parse errors: the HTML Standard requires reporting at least one parse error when there is one, and none
        // otherwise (https://html.spec.whatwg.org/multipage/parsing.html#parse-errors). The number of errors of the test files
        // is not comparable: they count the errors of the html5lib implementation (e.g. two errors for one character
        // reference, one error per foster-parented character, errors the specification does not define). Some files do not
        // list the errors: a document without DOCTYPE always has one (https://html.spec.whatwg.org/multipage/parsing.html#the-initial-insertion-mode).
        var hasErrors = test.ExpectedErrors.Count > 0 || (test.FragmentContext is null && !test.Data.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasErrors == errors.Count > 0, $"Expected parse errors:\n{string.Join('\n', test.ExpectedErrors)}\nActual:\n{string.Join('\n', errors)}");
    }

    // Found by comparing the fuzzing corpus with Chromium (tools/Meziantou.Framework.WebPlatform.ChromiumDiff), which deviates from the specification on these
    // inputs; the tree construction tests do not cover them
    [Theory]
    // search is in the special category, so it is the furthest block of the adoption agency algorithm (Chromium treats it as
    // an ordinary element and moves the p element instead)
    [InlineData("<a><search>x<p>y<a>z", """
        | <html>
        |   <head>
        |   <body>
        |     <a>
        |     <search>
        |       <a>
        |         "x"
        |       <p>
        |         <a>
        |           "y"
        |         <a>
        |           "z"

        """)]
    // A U+FFFD character token (not a NUL) sets the frameset-ok flag to "not ok" (Chromium ignores U+FFFD as it ignores U+0000)
    [InlineData("\U0000FFFD<frameset><frame>", "| <html>\n|   <head>\n|   <body>\n|     \"\U0000FFFD\"\n")]
    // A formatting element with an is attribute is reconstructed as the other ones (Chromium does not reconstruct it)
    [InlineData("<strong><font is></strong>x", "| <html>\n|   <head>\n|   <body>\n|     <strong>\n|       <font>\n|         is=\"\"\n|     <font>\n|       is=\"\"\n|       \"x\"\n")]
    // The end tags are not adjusted in foreign content, so </foreignobject> closes the HTML foreignobject element (Chromium and
    // WebKit adjust it to </foreignObject>)
    [InlineData("<foreignobject><svg></foreignobject>x", "| <html>\n|   <head>\n|   <body>\n|     <foreignobject>\n|       <svg svg>\n|     \"x\"\n")]
    // The whitespace in table text is inserted without reconstructing the active formatting elements (Chromium and WebKit
    // insert it in a new b element)
    [InlineData("<template><tr><b><tbody> ", "| <html>\n|   <head>\n|     <template>\n|       content\n|         <tr>\n|         <b>\n|         \" \"\n|   <body>\n")]
    public void ParseWhereChromiumDiffers(string input, string expectedTree)
    {
        var document = HtmlDocumentParser.ParseDocument(input);
        Assert.Equal(expectedTree.ReplaceLineEndings("\n"), TreeDumper.Dump(document));
    }

    // Found by comparing the fragments with Chromium (tools/Meziantou.Framework.WebPlatform.ChromiumDiff); the context is the
    // namespace prefix (svg, math) and the local name of the context element, followed by its attributes (name=value)
    [Theory]
    // The start tag token of the context element has the attributes of the element, so an annotation-xml context element
    // whose encoding is text/html or application/xhtml+xml is an HTML integration point
    [InlineData("math annotation-xml encoding=text/html", "<a>x", "| <a>\n|   \"x\"\n")]
    [InlineData("math annotation-xml encoding=APPLICATION/XHTML+XML", "<a>x", "| <a>\n|   \"x\"\n")]
    [InlineData("math annotation-xml encoding=text/plain", "<a>x", "| <math a>\n|   \"x\"\n")]
    [InlineData("math annotation-xml", "<a>x", "| <math a>\n|   \"x\"\n")]
    // The adjusted current node is the context element when the stack of open elements has only the html element, so this is a
    // CDATA section in an SVG or MathML context (Chromium and WebKit parse a bogus comment)
    [InlineData("svg path", "<![CDATA[x]]>", "| \"x\"\n")]
    // An end tag in foreign content is ignored when the stack of open elements has only the html element, so the a element stays
    // in the list of active formatting elements (Chromium and WebKit run the adoption agency algorithm)
    [InlineData("math math", "<strong><a></strong></a><big>", "| <strong>\n|   <a>\n| <a>\n|   <big>\n")]
    // The characters in a table row of a template are inserted in the template contents (Chromium ignores them in a colgroup context)
    [InlineData("colgroup", "<template><tr>y", "| <template>\n|   content\n|     <tr>\n|     \"y\"\n")]
    // The 'in template' insertion mode inserts the table elements after an element processed with the 'in head' rules
    // (Chromium and WebKit ignore them)
    [InlineData("template", "<title></title><td>", "| <title>\n| <td>\n")]
    public void ParseFragmentFoundByChromiumDiff(string context, string input, string expectedTree)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var parts = context.Split(' ');
        var (namespaceUri, nameIndex) = parts[0] switch
        {
            "svg" => ("http://www.w3.org/2000/svg", 1),
            "math" => ("http://www.w3.org/1998/Math/MathML", 1),
            _ => ("http://www.w3.org/1999/xhtml", 0),
        };
        var element = document.CreateElementNS(namespaceUri, parts[nameIndex]);
        foreach (var attribute in parts.Skip(nameIndex + 1))
        {
            var separator = attribute.IndexOf('=', StringComparison.Ordinal);
            element.SetAttribute(attribute[..separator], attribute[(separator + 1)..]);
        }

        element.InnerHtml = input;
        Assert.Equal(expectedTree, TreeDumper.Dump(element is HtmlTemplateElement template ? template.Content : element));
    }
}

/// <summary>Serializes a tree in the html5lib test format.</summary>
internal static class TreeDumper
{
    public static string Dump(Node node)
    {
        var builder = new StringBuilder();
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            Dump(builder, child, 0);
        }

        return builder.ToString();
    }

    private static void Dump(StringBuilder builder, Node node, int depth)
    {
        builder.Append("| ").Append(' ', depth * 2);
        switch (node)
        {
            case DocumentType doctype:
                builder.Append("<!DOCTYPE ").Append(doctype.Name);
                if (doctype.PublicId.Length > 0 || doctype.SystemId.Length > 0)
                {
                    builder.Append(" \"").Append(doctype.PublicId).Append("\" \"").Append(doctype.SystemId).Append('"');
                }

                builder.Append(">\n");
                break;

            case Element element:
            {
                builder.Append('<');
                if (element.NamespaceUri == "http://www.w3.org/2000/svg")
                {
                    builder.Append("svg ");
                }
                else if (element.NamespaceUri == "http://www.w3.org/1998/Math/MathML")
                {
                    builder.Append("math ");
                }

                builder.Append(element.LocalName).Append(">\n");
                var attributes = new List<(string Name, string Value)>();
                for (var i = 0; i < element.AttributeCount; i++)
                {
                    var ns = element.GetAttributeNamespace(i);
                    var prefix = ns switch
                    {
                        "http://www.w3.org/1999/xlink" => "xlink ",
                        "http://www.w3.org/XML/1998/namespace" => "xml ",
                        "http://www.w3.org/2000/xmlns/" => "xmlns ",
                        _ => "",
                    };
                    attributes.Add((prefix + element.GetAttributeLocalName(i), element.GetAttributeValue(i)));
                }

                foreach (var (name, value) in attributes.OrderBy(a => a.Name, StringComparer.Ordinal))
                {
                    builder.Append("| ").Append(' ', (depth + 1) * 2).Append(name).Append("=\"").Append(value).Append("\"\n");
                }

                if (element is HtmlTemplateElement template)
                {
                    builder.Append("| ").Append(' ', (depth + 1) * 2).Append("content\n");
                    for (var child = template.Content.FirstChild; child is not null; child = child.NextSibling)
                    {
                        Dump(builder, child, depth + 2);
                    }
                }

                for (var child = element.FirstChild; child is not null; child = child.NextSibling)
                {
                    Dump(builder, child, depth + 1);
                }

                break;
            }

            case Text text:
                builder.Append('"').Append(text.Data).Append("\"\n");
                break;

            case Comment comment:
                builder.Append("<!-- ").Append(comment.Data).Append(" -->\n");
                break;

            case ProcessingInstruction pi:
                builder.Append("<?").Append(pi.Target).Append(' ').Append(pi.Data).Append("?>\n");
                break;

            default:
                throw new InvalidOperationException($"Unexpected node {node.GetType()}");
        }
    }
}
