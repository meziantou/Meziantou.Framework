using System.Text;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Html.Parsing;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The tentative <c>src</c> attribute of a template that patches content (the declarative partial updates,
/// https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md): the content is fetched from
/// the URL instead of the children of the template, and parsed into the target as it arrives (or at once with
/// <c>buffer</c>), asynchronously: the parser of the document is not blocked.
/// </summary>
public partial class HtmlTemplateElement
{
    /// <summary>The fetch of the content of the template (see <see cref="StartSourcePatch"/>), or null.</summary>
    private SourcePatch? _sourcePatch;

    /// <summary>
    /// Fetches the content of a template that patches content with a <c>src</c> attribute, when the parser inserts it (the
    /// template stays in the document while its content is fetched, and its children are ignored): a CORS request ("a CORS-only
    /// request") checked by the content security policies as a script (<c>script-src</c>, with the <c>nonce</c> of the
    /// template), with its <c>integrity</c> and <c>referrerpolicy</c> attributes. When the response arrives, the content between
    /// the markers is replaced by the parsed content (the scripts run, unless the content is sanitized); at the end of the
    /// content, the markers and the template are removed. A failed fetch changes nothing. Removing the template cancels the fetch
    /// (the content already inserted stays).
    /// </summary>
    internal void StartSourcePatch(string src, ProcessingInstruction start, ProcessingInstruction? end, bool buffered, bool sanitizes)
    {
        var document = NodeDocument;
        if (Reflection.EncodingParseUrl(document, src) is not { } url || document.EventLoop is null)
            return;

        var integrity = GetAttributeValue("integrity") ?? "";
        var referrerPolicy = Infra.InfraAscii.ToAsciiLowercase(GetAttributeValue("referrerpolicy") ?? "");
        var request = SubresourceFetching.CreateRequest(document, url, RequestDestination.Script, cors: true, ReferrerPolicies.IsValid(referrerPolicy) ? referrerPolicy : "", this, integrity: integrity);
        request.Nonce = ContentSecurityPolicy.GetNonceMetadata(this);

        // The content is parsed as it arrives, unless it is buffered or its integrity is checked (the whole body is needed)
        request.StreamsBody = !buffered && integrity.Length == 0;
        var patch = new SourcePatch(start, end, buffered, sanitizes);
        _sourcePatch = patch;
        if (!SubresourceFetching.Fetch(document, request, resource => OnSourceResponse(patch, resource), initiatorType: "template", patch.Cancellation.Token))
        {
            _sourcePatch = null;
            patch.Cancellation.Dispose();
        }
    }

    /// <summary>Cancels the fetch of the content (the template is removed).</summary>
    private void CancelSourcePatch()
    {
        if (_sourcePatch is not { } patch)
            return;

        _sourcePatch = null;
        patch.Canceled = true;
        patch.Writer?.Abort();
        patch.Cancellation.Cancel();
    }

    private void OnSourceResponse(SourcePatch patch, DocumentResource? resource)
    {
        if (patch.Canceled)
            return;

        if (resource is null || resource.StatusCode is < 200 or > 299)
        {
            // The content cannot be fetched: the target is unchanged
            EndSourcePatch(patch, completed: false);
            return;
        }

        Receive(patch, resource.ContentBytes is { } bytes ? bytes.Span : Encoding.UTF8.GetBytes(resource.Content ?? ""), flush: false);
        if (resource.RemainingContentBytes is { } remaining && NodeDocument.EventLoop is { } eventLoop)
        {
            ReceiveNextChunk(patch, eventLoop, remaining.GetAsyncEnumerator(patch.Cancellation.Token));
            return;
        }

        CompleteSourcePatch(patch);
    }

    /// <summary>Waits for the next chunk of the content, and parses it from a task of the event loop.</summary>
    private void ReceiveNextChunk(SourcePatch patch, DomEventLoop eventLoop, IAsyncEnumerator<ReadOnlyMemory<byte>> chunks)
    {
        ValueTask<bool> next;
        try
        {
            next = chunks.MoveNextAsync();
        }
#pragma warning disable CA1031 // An exception of the response is an interrupted connection
        catch (Exception ex)
#pragma warning restore CA1031
        {
            next = ValueTask.FromException<bool>(ex);
        }

        eventLoop.BeginOperation();
        _ = next.AsTask().ContinueWith(
            completed =>
            {
                eventLoop.QueueTask(() =>
                {
                    if (patch.Canceled)
                    {
                        _ = chunks.DisposeAsync().AsTask();
                        return;
                    }

                    if (completed.IsCompletedSuccessfully && completed.Result)
                    {
                        Receive(patch, chunks.Current.Span, flush: false);
                        ReceiveNextChunk(patch, eventLoop, chunks);
                        return;
                    }

                    _ = chunks.DisposeAsync().AsTask();
                    if (completed.IsCompletedSuccessfully)
                    {
                        CompleteSourcePatch(patch);
                    }
                    else
                    {
                        // An interrupted connection: the content received so far stays
                        patch.Writer?.Close();
                        EndSourcePatch(patch, completed: false);
                    }
                });
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Decodes the bytes of the content (UTF-8), and parses them into the target, unless the content is buffered.</summary>
    private void Receive(SourcePatch patch, ReadOnlySpan<byte> bytes, bool flush)
    {
        var chars = new char[patch.Decoder.GetCharCount(bytes, flush)];
        var count = patch.Decoder.GetChars(bytes, chars, flush);
        patch.Text.Append(chars, 0, count);
        if (!patch.Buffered && patch.Text.Length > 0)
        {
            Write(patch, patch.Text.ToString());
            patch.Text.Clear();
        }
    }

    /// <summary>Parses HTML into the target: the first chunk replaces the content between the markers.</summary>
    private void Write(SourcePatch patch, string html)
    {
        if (patch.Writer is null)
        {
            if (patch.Start.ParentNode is not { } target)
                return;

            ContentPatching.RemoveContentBetweenMarkers(patch.Start, patch.End);
            var end = patch.End is { } endMarker && ReferenceEquals(endMarker.ParentNode, target) ? endMarker : null;
            patch.Writer = patch.Sanitizes
                ? DynamicMarkup.StreamHtml(target, end, FragmentParserOptions.FromSafe(options: null), safe: true)
                : DynamicMarkup.StreamHtml(target, end, new FragmentParserOptions(Sanitizer: null, RunScripts: true), safe: false);
        }

        if (!patch.Writer.IsCompleted)
        {
            patch.Writer.Write(html);
        }
    }

    /// <summary>The end of the content: the buffered content is parsed at once, then the markers and the template are removed.</summary>
    private void CompleteSourcePatch(SourcePatch patch)
    {
        Receive(patch, [], flush: true);
        if (patch.Buffered || patch.Writer is null)
        {
            Write(patch, patch.Text.ToString());
        }

        if (patch.Canceled)
            return;

        patch.Writer?.Close();
        EndSourcePatch(patch, completed: true);
    }

    private void EndSourcePatch(SourcePatch patch, bool completed)
    {
        if (!ReferenceEquals(_sourcePatch, patch))
            return;

        _sourcePatch = null;
        patch.Cancellation.Dispose();
        if (!completed)
            return;

        patch.Start.RemoveFromParent();
        patch.End?.RemoveFromParent();
        RemoveFromParent();
    }

    /// <summary>The state of the fetch of the content of a template with a <c>src</c> attribute.</summary>
    private sealed class SourcePatch(ProcessingInstruction start, ProcessingInstruction? end, bool buffered, bool sanitizes)
    {
        public ProcessingInstruction Start { get; } = start;

        public ProcessingInstruction? End { get; } = end;

        public bool Buffered { get; } = buffered;

        public bool Sanitizes { get; } = sanitizes;

        public CancellationTokenSource Cancellation { get; } = new();

        public Decoder Decoder { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();

        /// <summary>The decoded content that is not parsed yet (the whole content when it is buffered).</summary>
        public StringBuilder Text { get; } = new();

        /// <summary>The parser of the content, created when the first chunk is parsed.</summary>
        public HtmlStreamWriter? Writer { get; set; }

        public bool Canceled { get; set; }
    }
}
