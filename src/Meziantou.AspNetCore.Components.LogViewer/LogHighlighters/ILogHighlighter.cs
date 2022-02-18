namespace Meziantou.AspNetCore.Components;

public interface ILogHighlighter
{
    IEnumerable<LogHighlighterResult> Process(string text);
}
