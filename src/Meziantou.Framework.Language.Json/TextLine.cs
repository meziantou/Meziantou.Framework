namespace Meziantou.Framework.Language.Json;

/// <summary>Describes a single logical line in a <see cref="SourceText"/> instance.</summary>
public readonly record struct TextLine(int LineNumber, int Start, int End, string Text);
