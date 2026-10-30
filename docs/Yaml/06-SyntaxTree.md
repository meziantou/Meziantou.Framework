---
title: Syntax tree
description: Parse YAML into tokens that keep the comments and the whitespace, with the position of each one, to build linters, formatters and editors.
---

[YamlSyntaxTree](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree) is the lossless representation of a YAML text: it keeps every character, including the comments, the
whitespace and the line breaks, and gives the position of every token. Use it for tools that must not disturb a file,
such as a linter, a formatter, or a program that changes one value of a configuration file. Its types are in the
[Meziantou.Framework.Yaml.Syntax](cref:Meziantou.Framework.Yaml.Syntax) namespace.

## Parse a text

[YamlSyntaxTree.Parse](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree.Parse) returns the tree. [Tokens](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree.Tokens) lists the tokens in order, each with its [Kind](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxToken.Kind), its [Text](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxToken.Text) and its
`Span`; [ToFullString](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree.ToFullString) returns the original text:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Syntax/YamlSyntaxTreeTests.cs;region=YamlSyntaxTree_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = """
    # deployment settings
    replicas: 3 # keep in sync with the chart
    """;

var tree = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = true });

var tokens = new List<string>();
foreach (var token in tree.Tokens)
{
    tokens.Add($"{token.Kind} {token.Span} {token.Text}");
}

Assert.Equal(
[
    "StreamStart 0:0 ",
    "CommentTrivia 0:21 # deployment settings",
    "NewLineTrivia 21:22 \n",
    "BlockMappingStart 22:22 ",
    "Key 22:22 ",
    "Scalar 22:30 replicas",
    "Value 30:31 :",
    "WhitespaceTrivia 31:32  ",
    "Scalar 32:33 3",
    "WhitespaceTrivia 33:34  ",
    "CommentTrivia 34:63 # keep in sync with the chart",
    "BlockEnd 63:63 ",
    "StreamEnd 63:63 ",
], tokens);
Assert.Equal(yaml, tree.ToFullString());
```
<!-- endref -->

- A `Span` goes from a [Start](cref:Meziantou.Framework.Yaml.Syntax.YamlSourceSpan.Start) to an [End](cref:Meziantou.Framework.Yaml.Syntax.YamlSourceSpan.End) position. A position ([Mark](cref:Meziantou.Framework.Yaml.Mark)) has an [Index](cref:Meziantou.Framework.Yaml.Mark.Index) in the text, and a [Line](cref:Meziantou.Framework.Yaml.Mark.Line) and
  a [Column](cref:Meziantou.Framework.Yaml.Mark.Column), which start at `0`.
- The comments, the whitespace and the line breaks are trivia tokens: [CommentTrivia](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxKind.CommentTrivia), [WhitespaceTrivia](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxKind.WhitespaceTrivia) and
  [NewLineTrivia](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxKind.NewLineTrivia). [IncludeTrivia](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxOptions.IncludeTrivia) is `true` by default; set it to `false` to get the significant tokens only.
- Some tokens mark a structure and have no text, such as [BlockMappingStart](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxKind.BlockMappingStart) or [Key](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxKind.Key).
- [Root](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree.Root) is the root node of the tree. Its `Span` covers the document, and its [FullSpan](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxNode.FullSpan) also covers the trivia
  around it.
- [WriteTo](cref:Meziantou.Framework.Yaml.Syntax.YamlSyntaxTree.WriteTo) writes the original text to a `TextWriter`.

## Edit a file without changing its layout

The positions of the tokens let you change a part of the text and keep the rest as it is, comments included:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Syntax/YamlSyntaxTreeTests.cs;region=YamlSyntaxTree_EditValue;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = """
    # deployment settings
    replicas: 3 # keep in sync with the chart
    image: nginx
    """;

// Without trivia, the tokens are the significant ones: Key, Scalar, Value, Scalar, ...
var tokens = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = false }).Tokens;
var keyIndex = tokens.ToList().FindIndex(token => token.Kind is YamlSyntaxKind.Scalar && token.Text == "replicas");
var value = tokens[keyIndex + 2];
Assert.Equal(YamlSyntaxKind.Scalar, value.Kind);

// Replace the text of the value only: the comments and the layout are kept
var updated = string.Concat(yaml.AsSpan(0, value.Span.Start.Index), "5", yaml.AsSpan(value.Span.End.Index));
Assert.Equal("""
    # deployment settings
    replicas: 5 # keep in sync with the chart
    image: nginx
    """, updated);
```
<!-- endref -->

The [document object model](05-DocumentObjectModel.md) is easier to change, but writes the whole document again,
without its comments.
