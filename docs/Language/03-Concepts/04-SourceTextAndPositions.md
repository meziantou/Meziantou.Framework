---
title: Source text and positions
description: The text of a tree, character spans, lines and columns, and the location of a diagnostic.
---

Positions in a tree are offsets in its text, counted in UTF-16 code units from 0. The types of this page turn them into
lines and columns, and the other way around.

## The text of a tree

[SourceText](cref:Meziantou.Framework.Language.SourceText) is the text a tree was parsed from: `tree.GetText()` returns it. You can also create one with
[SourceText.From](cref:Meziantou.Framework.Language.SourceText.From) and pass it to `ParseText`. It gives the text with `Text` or [ToString()](cref:Meziantou.Framework.Language.SourceText.ToString), a part of it with
`ToString(span)`, and its lines with [Lines](cref:Meziantou.Framework.Language.SourceText.Lines) and `GetLine(position)`:

<!-- ref:../../../tests/Meziantou.Framework.Language.Tests/SourceTextTests.cs;region=TextLine_FromSourceText;format=md-fenced-code-block;dedent=true -->
```csharp
var line = SourceText.From("first\nsecond").Lines[1];
var text = line.Text;
Assert.Equal("second", text);
Assert.Equal(1, line.LineNumber);
Assert.Equal(6, line.Start);
```
<!-- endref -->

A [TextLine](cref:Meziantou.Framework.Language.TextLine) has its zero-based [LineNumber](cref:Meziantou.Framework.Language.TextLine.LineNumber), the [Start](cref:Meziantou.Framework.Language.TextLine.Start) and [End](cref:Meziantou.Framework.Language.TextLine.End) offsets of its text, and its `Text`, without the line
break.

## Spans

A [TextSpan](cref:Meziantou.Framework.Language.TextSpan) is a range of characters: a [Start](cref:Meziantou.Framework.Language.TextSpan.Start) and a [Length](cref:Meziantou.Framework.Language.TextSpan.Length), or [End](cref:Meziantou.Framework.Language.TextSpan.End), which is exclusive. Create one from a start
and a length, or from two bounds:

<!-- ref:../../../tests/Meziantou.Framework.Language.Tests/TextSpanTests.cs;region=TextSpan_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var span = new TextSpan(start: 5, length: 3);
var sameSpan = TextSpan.FromBounds(5, 8);
Assert.Equal(span, sameSpan);
Assert.Equal(8, span.End);
```
<!-- endref -->

[Contains](cref:Meziantou.Framework.Language.TextSpan.Contains), [OverlapsWith](cref:Meziantou.Framework.Language.TextSpan.OverlapsWith), [IntersectsWith](cref:Meziantou.Framework.Language.TextSpan.IntersectsWith), [Overlap](cref:Meziantou.Framework.Language.TextSpan.Overlap) and [Intersection](cref:Meziantou.Framework.Language.TextSpan.Intersection) compare spans. Every node, token and trivia
has a [Span](cref:Meziantou.Framework.Language.SyntaxNode.Span) and a [FullSpan](cref:Meziantou.Framework.Language.SyntaxNode.FullSpan): see [Trivia](03-Trivia.md#span-and-fullspan).

## Lines and columns

A [Location](cref:Meziantou.Framework.Language.Location) pairs a span with the text it indexes into, so it can give lines and columns. [GetLineSpan()](cref:Meziantou.Framework.Language.Location.GetLineSpan) returns a
[LinePositionSpan](cref:Meziantou.Framework.Language.LinePositionSpan), whose [Start](cref:Meziantou.Framework.Language.LinePositionSpan.Start) and [End](cref:Meziantou.Framework.Language.LinePositionSpan.End) are [LinePosition](cref:Meziantou.Framework.Language.LinePosition)s, with a zero-based [Line](cref:Meziantou.Framework.Language.LinePosition.Line) and [Character](cref:Meziantou.Framework.Language.LinePosition.Character):

<!-- ref:../../../tests/Meziantou.Framework.Language.Tests/LocationTests.cs;region=Location_GetLineSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "one\ntwo\nthree line here";
var location = new Location(new TextSpan(15, 7), SourceText.From(text));

// Lines and columns are zero-based
Assert.Equal("(2,7)-(2,14)", location.ToString());
Assert.Equal(2, location.GetLineSpan().Start.Line);
```
<!-- endref -->

Add one to both numbers to show them to a person, as editors count lines and columns from 1.

A tree gives the location or the line span of any span with `GetLocation(span)` and `GetLineSpan(span)`. A [Location](cref:Meziantou.Framework.Language.Location)
created without its text, such as [Location.None](cref:Meziantou.Framework.Language.Location.None), has no lines: its [GetLineSpan()](cref:Meziantou.Framework.Language.Location.GetLineSpan) returns `default`.

## Text changes

A [TextChange](cref:Meziantou.Framework.Language.TextChange) replaces a span with a new text. [SourceText.WithChanges](cref:Meziantou.Framework.Language.SourceText.WithChanges) applies changes to a text, and a tree's
[WithChanges](cref:Meziantou.Framework.Language.SourceText.WithChanges) applies them and parses the result:

<!-- ref:../../../tests/Meziantou.Framework.Language.Tests/SourceTextTests.cs;region=Language_SourceTextAndEdits;format=md-fenced-code-block;dedent=true -->
```csharp
var source = SourceText.From("<root/>");

var updated = source.WithChanges([new TextChange(new TextSpan(1, 4), "item")]);
Assert.Equal("<item/>", updated.Text);

var line = source.GetLine(position: 3);
Assert.Equal(0, line.LineNumber);
```
<!-- endref -->

The spans of the changes refer to the original text, whatever their order: the changes are applied from the end of the
text backwards. A change that reaches past the end of the text is ignored.

To get the changes between two versions of a tree, see
[Get the text changes](06-EditingTrees.md#get-the-text-changes).
