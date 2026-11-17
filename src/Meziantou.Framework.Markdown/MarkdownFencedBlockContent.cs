namespace Meziantou.Framework.Markdown;

/// <summary>What the lines between the fences of a <see cref="MarkdownFencedBlockExtension"/> are.</summary>
public enum MarkdownFencedBlockContent
{
    /// <summary>Text, as the lines of a fenced code block: <see cref="MarkdownExtensionBlockSyntax.Lines"/>.</summary>
    RawLines,

    /// <summary>Markdown, as the content of a custom container: <see cref="MarkdownExtensionBlockSyntax.Blocks"/>.</summary>
    Blocks,
}
