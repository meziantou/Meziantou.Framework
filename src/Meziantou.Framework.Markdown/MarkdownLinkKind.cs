namespace Meziantou.Framework.Markdown;

/// <summary>How a link or an image gives its destination.</summary>
public enum MarkdownLinkKind
{
    /// <summary>After its text: <c>[text](destination "title")</c>.</summary>
    Inline,

    /// <summary>In a definition named by a label after its text: <c>[text][label]</c>.</summary>
    Full,

    /// <summary>In a definition named by its text, followed by empty brackets: <c>[text][]</c>.</summary>
    Collapsed,

    /// <summary>In a definition named by its text: <c>[text]</c>.</summary>
    Shortcut,
}
