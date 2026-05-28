namespace Meziantou.Framework.SyntaxHighlighting.Engine;

/// <summary>
/// A keyword entry. <c>Scope</c> is the scope name to wrap the keyword in, or
/// <see langword="null"/> when the keyword should be emitted as plain text
/// (highlight.js's <c>_</c> sentinel).
/// </summary>
internal readonly record struct KeywordHit(string? Scope, int Relevance);
