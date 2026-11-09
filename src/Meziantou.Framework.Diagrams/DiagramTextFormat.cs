namespace Meziantou.Framework.Diagrams;

/// <summary>How the content of a <see cref="DiagramText"/> is read.</summary>
public enum DiagramTextFormat
{
    /// <summary>Plain text: a line feed starts a new line, and every other character is drawn as is.</summary>
    Plain,

    /// <summary>
    /// Markdown, as the markdown strings of Mermaid: <c>**bold**</c> or <c>__bold__</c>, <c>*italic*</c> or <c>_italic_</c>,
    /// a backslash before a character to draw it as is, and line feeds. The text wraps at the wrapping width of the diagram.
    /// </summary>
    Markdown,
}
