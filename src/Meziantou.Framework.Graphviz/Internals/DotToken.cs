namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>A token of a DOT text. <see cref="Value"/> is the text of an identifier, a numeral or a string, without its quotes or its angle brackets.</summary>
internal readonly record struct DotToken(DotTokenKind Kind, string Value, int Start);
