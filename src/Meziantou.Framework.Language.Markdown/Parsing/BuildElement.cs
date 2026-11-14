namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// A node or a token found by the parser, before the tree is built. A token is a range of the text: the characters that no
/// token covers become trivia when the tree is built.
/// </summary>
internal abstract class BuildElement;
