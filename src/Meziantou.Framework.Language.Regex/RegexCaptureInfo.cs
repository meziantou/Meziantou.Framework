namespace Meziantou.Framework.Language.Regex;

/// <summary>Describes one capture group of a pattern.</summary>
/// <param name="Number">The group number the engine assigns.</param>
/// <param name="Name">The group name. For an unnamed group this is the number written out.</param>
/// <param name="Span">The span of the construct that declares the group.</param>
public readonly record struct RegexCaptureInfo(int Number, string Name, TextSpan Span);
