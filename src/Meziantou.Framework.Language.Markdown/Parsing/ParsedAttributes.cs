namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>Generic attributes: an id, classes, and properties whose value is <see langword="null"/> for a boolean one.</summary>
internal sealed record ParsedAttributes(string? Id, List<string> Classes, List<KeyValuePair<string, string?>> Properties);
