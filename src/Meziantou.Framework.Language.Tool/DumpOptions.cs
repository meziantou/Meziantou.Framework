namespace Meziantou.Framework.LanguageTool;

/// <summary>What the dump keeps. Everything is emitted unless a <c>--no-*</c> option turns it off.</summary>
internal sealed record DumpOptions
{
    public bool IncludeTokens { get; init; } = true;
    public bool IncludeTrivia { get; init; } = true;
    public bool IncludeText { get; init; } = true;
    public bool IncludeSpans { get; init; } = true;
}
