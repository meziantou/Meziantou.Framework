# Meziantou.Framework.Language.Xml

`Meziantou.Framework.Language.Xml` provides immutable XML syntax tree APIs focused on **roundtrip-safe** XML processing:

- parse XML into a syntax tree without reformatting it
- navigate/query nodes with XPath
- edit targeted nodes
- save back while preserving untouched text and formatting
- support for XML namespaces in queries and edits
- support invalid XML documents (for example with unclosed tags) and report diagnostics

```csharp
using System.Xml;
using Meziantou.Framework.Language.Xml;

const string xml = "<root><item version='1.0.0' /></root>";

var tree = XmlSyntaxTree.ParseText(xml);

// Navigate the tree
tree.Root.DescendantNodes().OfType<XmlAttributeSyntax>().First().Value;

// Navigate the tree with XPath
var itemNode = tree.Root.SelectSingleNode("//item");

// Update a node
var updatedRoot = tree.Root.ReplaceNode(
    itemNode,
    XmlSyntaxFactory.Element("newItem").WithAttribute("version", "2.0.0"));

// Update the tree with XPath
var updatedRoot = tree.Root.ReplaceNode(
    "//item/@version",
    node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

// Get the updated XML
var updatedXml = updatedRoot.ToFullString();
```
