namespace Meziantou.Framework.SyntaxHighlighting.Engine;

// Per-language predicate consulted at keyword highlight time. Receives the full
// input text and the absolute index of the matched word so language-specific
// rules can examine preceding/following context (e.g. LINQ clause detection in
// C# where the `from <id> in` anchor may live before an earlier buffer flush).
internal delegate bool KeywordValidator(string input, int index, ReadOnlySpan<char> word);
