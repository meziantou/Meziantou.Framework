namespace Meziantou.AspNetCore.Components;

/// <summary>Defines a highlighter that can process and mark parts of log messages for special rendering.</summary>
public interface ILogHighlighter
{
    /// <summary>Processes the specified text and returns a collection of highlight results.</summary>
    /// <param name="text">The text to process.</param>
    /// <returns>A collection of <see cref="LogHighlighterResult"/> objects indicating which parts of the text should be highlighted.</returns>
    IEnumerable<LogHighlighterResult> Process(string text);
}
