namespace Meziantou.Framework.Language.Fuzz;

/// <summary>The tokens of a tree, in source order, and the number of trivia they carry.</summary>
internal sealed record TreeShape(List<SyntaxToken> Tokens, int TriviaCount);
