---
title: Options and captures
description: Pass the options a pattern is used with, read the options in effect at each node, extended mode, and the numbering of capture groups.
---

## Pass the options of the pattern

The options an engine receives with a pattern change how it reads it: in extended mode, whitespace and `#` comments are
not part of the pattern. Pass them in [RegexParseOptions.PatternOptions](cref:Meziantou.Framework.Language.Regex.RegexParseOptions.PatternOptions):

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_ParseOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = "a b # comment";
var options = new RegexParseOptions(RegexDialect.Net)
{
    PatternOptions = RegexPatternOptions.IgnorePatternWhitespace,
    MaxRecursionDepth = 64,
};

var tree = RegexSyntaxTree.ParseText(pattern, options);

// In extended mode, the whitespace and the comment are trivia
Assert.HasCount(2, tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>());
```
<!-- endref -->

[RegexPatternOptions](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions) combines the options of every dialect, from [IgnoreCase](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.IgnoreCase) and [IgnorePatternWhitespace](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.IgnorePatternWhitespace) to the
JavaScript flags such as [Unicode](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.Unicode), [UnicodeSets](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.UnicodeSets), [Global](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.Global) and [Sticky](cref:Meziantou.Framework.Language.Regex.RegexPatternOptions.Sticky). `tree.PatternOptions` returns the options a
tree was parsed with, flags of a JavaScript literal included.

When your pattern goes to a `Regex`, convert its `RegexOptions` with [RegexOptionsInterop](cref:Meziantou.Framework.Language.Regex.RegexOptionsInterop), which keeps only the options
that change how the pattern is read:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_RegexOptionsInterop;format=md-fenced-code-block;dedent=true -->
```csharp
var patternOptions = RegexOptionsInterop.ToPatternOptions(RegexOptions.IgnoreCase | RegexOptions.Compiled);
Assert.Equal(RegexPatternOptions.IgnoreCase, patternOptions);
```
<!-- endref -->

[RegexOptionsInterop.ToRegexOptions](cref:Meziantou.Framework.Language.Regex.RegexOptionsInterop.ToRegexOptions) converts the other way.

## Limit the nesting

[MaxRecursionDepth](cref:Meziantou.Framework.Language.Regex.RegexParseOptions.MaxRecursionDepth), 128 by default, bounds how deeply the parser goes into nested groups and classes, so deeply nested
input cannot overflow the stack. What nests deeper is reported as `REGEX0200` and kept as skipped text, which usually
gives a second diagnostic for the groups left open. A value below 1 throws where it is set.

## Options at each node

An inline option, such as `(?i)`, changes the options of what follows it. [Options](cref:Meziantou.Framework.Language.Regex.RegexSyntaxNode.Options) on a node gives the options in
effect at its first character:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_NodeOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("a(?i)b", RegexDialect.Net);
var literals = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();

Assert.Equal(RegexPatternOptions.None, literals[0].Options);
Assert.Equal(RegexPatternOptions.IgnoreCase, literals[1].Options);
```
<!-- endref -->

## Comments and extended mode

A pattern has no trivia, except in extended mode, which `(?x)` can switch on and off part-way through a pattern. Then,
whitespace and `#` comments are trivia; inside a character class, they stay literal. A `(?#…)` comment is trivia in
every mode. [DescendantComments()](cref:Meziantou.Framework.Language.Regex.RegexSyntaxNode.DescendantComments) returns the comments:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Comments;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexDialect.Net);

var comments = new List<string>();
foreach (var comment in tree.GetRoot().DescendantComments())
{
    comments.Add($"{comment.Span.Start}: {comment}");
}

Assert.Equal(["1: (?#note)"], comments);
```
<!-- endref -->

## Capture groups

`tree.Captures` lists the capture groups with their [Number](cref:Meziantou.Framework.Language.Regex.RegexCaptureInfo.Number), their [Name](cref:Meziantou.Framework.Language.Regex.RegexCaptureInfo.Name) and their [Span](cref:Meziantou.Framework.Language.Regex.RegexCaptureInfo.Span), numbered the way the engine
of the dialect numbers them. In .NET, that is not the order they are written in: named groups take the first free
numbers after the unnamed and the explicitly numbered ones. JavaScript and PCRE number every group where it stands, and
the branches of a PCRE branch reset group share their numbers:

<!-- ref:../../../tests/Meziantou.Framework.Language.Regex.Tests/RegexSyntaxTreeTests.cs;region=RegexSyntaxTree_Captures;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.Net);

var captures = new List<string>();
foreach (var capture in tree.Captures)
{
    captures.Add($"{capture.Number}: {capture.Name}");
}

Assert.Equal(["1: 1", "2: 2", "3: x"], captures);

// The same pattern in JavaScript or PCRE
var javaScriptTree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.JavaScript);
Assert.Equal(["1: 1", "2: x", "3: 3"], javaScriptTree.Captures.Select(capture => $"{capture.Number}: {capture.Name}"));
```
<!-- endref -->

A group without a name has its number as its [Name](cref:Meziantou.Framework.Language.Regex.RegexCaptureInfo.Name).
