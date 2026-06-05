# Meziantou.Framework.Xml

`Meziantou.Framework.Xml` provides immutable XML syntax tree APIs focused on **roundtrip-safe** XML processing:

- parse XML into a syntax tree without reformatting it,
- navigate/query nodes with XPath,
- edit targeted nodes,
- save back while preserving untouched text and formatting.

## Roundtrip parsing / edit / save

```csharp
using System.Xml;
using Meziantou.Framework.Xml;

const string xml = "<root xmlns:p='urn:pkg'><p:item version='1.0.0' /></root>";

var tree = XmlSyntaxTree.ParseText(xml);

var namespaces = new XmlNamespaceManager(new NameTable());
namespaces.AddNamespace("p", "urn:pkg");

var updatedRoot = tree.Root.ReplaceNode(
    "//p:item/@version",
    namespaces,
    static node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

var updatedXml = updatedRoot.ToFullString();
// untouched formatting is preserved
```

## How to use it

### 1. Parse

```csharp
var tree = XmlSyntaxTree.ParseText(xmlText);
var diagnostics = tree.Diagnostics; // parse errors/warnings
```

### 2. Query nodes (XPath)

```csharp
var node = tree.Root.SelectSingleSyntaxNode("//package/@version");
```

For namespaced queries, pass an `IXmlNamespaceResolver` (for example `XmlNamespaceManager`):

```csharp
var ns = new XmlNamespaceManager(new NameTable());
ns.AddNamespace("p", "urn:pkg");

var node = tree.Root.SelectSingleSyntaxNode("//p:item/@version", ns);
```

### 3. Edit

```csharp
var updated = tree.Root.ReplaceNode(
    "//package/@version",
    static node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));
```

You can also edit element text:

```csharp
var updated = tree.Root.ReplaceNode(
    "//package",
    static node => ((XmlElementSyntax)node).WithInnerText("new-content"));
```

### 4. Save

```csharp
var output = updated.ToFullString();
```
