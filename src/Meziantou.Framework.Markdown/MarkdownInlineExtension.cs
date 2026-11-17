namespace Meziantou.Framework.Markdown;

/// <summary>
/// An inline syntax that starts at one of <see cref="TriggerCharacters"/>, such as <c>@mention</c> or <c>{{variable}}</c>.
/// The parser calls <see cref="TryParse"/> at each trigger character, before the built-in syntaxes, and the construct
/// becomes a <see cref="MarkdownExtensionInlineSyntax"/>.
/// </summary>
/// <remarks>
/// <para>
/// The extensions of a character are tried in the order of <see cref="MarkdownParseOptions.SyntaxExtensions"/>, then the
/// built-in syntaxes. A construct is atomic, as a code span: the delimiters of emphasis and the brackets of links do not
/// match across its edges, and the content added with <see cref="MarkdownInlineParseContext.AddInlines"/> is parsed on its
/// own. The parser does not call extensions in code spans, in autolinks, in raw HTML, or after a backslash.
/// </para>
/// <para>
/// One instance parses every document, possibly on several threads at once: keep no state between calls.
/// </para>
/// </remarks>
public abstract class MarkdownInlineExtension : MarkdownSyntaxExtension
{
    /// <summary>Initializes a new instance of the <see cref="MarkdownInlineExtension"/> class.</summary>
    /// <param name="name">The name of the extension, which the nodes of its constructs have.</param>
    /// <param name="triggerCharacters">The characters where a construct can start: not whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="triggerCharacters"/> is empty, or <paramref name="triggerCharacters"/> has whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="triggerCharacters"/> is <see langword="null"/>.</exception>
    protected MarkdownInlineExtension(string name, string triggerCharacters)
        : base(name)
    {
        ValidateTriggerCharacters(triggerCharacters);
        TriggerCharacters = triggerCharacters;
    }

    internal static void ValidateTriggerCharacters(string triggerCharacters)
    {
        ArgumentException.ThrowIfNullOrEmpty(triggerCharacters);
        foreach (var c in triggerCharacters)
        {
            if (char.IsWhiteSpace(c) || c == '\0')
            {
                throw new ArgumentException($"The trigger character U+{(int)c:X4} is whitespace.", nameof(triggerCharacters));
            }
        }
    }

    /// <summary>Gets the characters where a construct can start.</summary>
    public string TriggerCharacters { get; }

    /// <summary>Reads a construct at a trigger character, the first character of <see cref="MarkdownInlineParseContext.Text"/>.</summary>
    /// <param name="context">The text from the trigger character, where the construct reports its parts and its length.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a construct, after calling <see cref="MarkdownInlineParseContext.Complete"/>;
    /// <see langword="false"/> to let the next extension, or the built-in syntaxes, read the text.
    /// </returns>
    public abstract bool TryParse(ref MarkdownInlineParseContext context);
}
