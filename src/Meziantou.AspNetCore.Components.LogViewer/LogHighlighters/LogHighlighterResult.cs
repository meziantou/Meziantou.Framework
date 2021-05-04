namespace Meziantou.AspNetCore.Components
{
    public record LogHighlighterResult(int Index, int Length, int Priority)
    {
        public string? Link { get; init; }
        public string? ReplacementText { get; init; }
        public string? Title { get; init; }
    }
}
