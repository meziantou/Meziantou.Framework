namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The definitions of a document that a part of its text is parsed with, so that the part reads the references to them as the
/// document reads them: the labels of the link reference definitions, of the footnotes and of the headings that a reference
/// link can link to, and the abbreviations with the length of what they stand for.
/// </summary>
internal sealed record DocumentDefinitions(IReadOnlyList<string> References, IReadOnlyList<string> Footnotes, IReadOnlyList<KeyValuePair<string, int>> Abbreviations, IReadOnlyList<string> HeadingLabels);
