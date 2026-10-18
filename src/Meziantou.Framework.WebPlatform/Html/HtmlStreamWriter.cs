using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The writer of the HTML streamed into an element or a shadow root (https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html),
/// returned by <see cref="Element.StreamAppendHtmlUnsafe"/> and the other streaming methods: the chunks are parsed as they
/// are written, by a fragment parser that inserts the nodes directly into the target (before the reference child), so the
/// <c>template for</c> elements patch the content as it arrives. In JavaScript, the methods return a <c>WritableStream</c>
/// whose sink is this writer.
/// </summary>
[Experimental(ExperimentalApis.StreamingHtmlInsertion, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class HtmlStreamWriter
{
    private readonly Node _target;
    private readonly Node? _referenceChild;
    private readonly HtmlTreeBuilder _builder;
    private readonly HtmlTokenizer _tokenizer;

    internal HtmlStreamWriter(Node target, Node? referenceChild, HtmlTreeBuilder builder, HtmlTokenizer tokenizer)
    {
        _target = target;
        _referenceChild = referenceChild;
        _builder = builder;
        _tokenizer = tokenizer;
    }

    /// <summary>Gets whether the stream is closed or aborted: the writer cannot be written to anymore.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Parses a chunk of HTML, after the previous chunks (the write algorithm of https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html).</summary>
    /// <exception cref="InvalidOperationException">The writer is closed or aborted.</exception>
    /// <exception cref="DomException">The reference child is no longer a child of the target: the parser is aborted.</exception>
    public void Write(string chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ThrowIfCompleted();
        if (_referenceChild is not null && !ReferenceEquals(_referenceChild.ParentNode, _target))
        {
            Abort();
            throw new DomException("The node before which the HTML is inserted is no longer a child of the target", "HierarchyRequestError");
        }

        using var ceReactions = CEReactionsScope.Enter();
        _tokenizer.Insert(0, chunk);
        _builder.Run();
    }

    /// <summary>Parses the end of the input (the close algorithm of https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html).</summary>
    /// <exception cref="InvalidOperationException">The writer is closed or aborted.</exception>
    public void Close()
    {
        ThrowIfCompleted();
        IsCompleted = true;
        using var ceReactions = CEReactionsScope.Enter();
        _tokenizer.EofInserted = true;
        _builder.Run();
    }

    /// <summary>Aborts the parser: the rest of the input is not parsed, and the script elements that are still open never run.</summary>
    public void Abort()
    {
        if (IsCompleted)
            return;

        IsCompleted = true;
        _builder.Abort();
    }

    private void ThrowIfCompleted()
    {
        if (IsCompleted)
            throw new InvalidOperationException("The HTML stream is closed");
    }
}
