using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The response of an emulated handler of the WPT server (the <c>response</c> object of wptserve).</summary>
internal sealed class WptHandlerResponse
{
    public int Status { get; set; } = 200;

    /// <summary>The status message, or null for the reason phrase of the status.</summary>
    public string? StatusText { get; set; }

    /// <summary>The headers, as byte strings.</summary>
    public List<KeyValuePair<string, string>> Headers { get; } = [];

    public byte[] Body { get; set; } = [];

    /// <summary>The delay of the response (<c>time.sleep()</c>), measured by the virtual clock.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>
    /// The delay after which the handler runs again (measured by the virtual clock), or null: a handler that waits for a value
    /// of the stash (a <c>time.sleep()</c> loop, e.g. <c>reporting/resources/report.py</c>) runs again with
    /// <see cref="WptHandlerRequest.PollingElapsed"/> increased by the delay, and its response is ignored.
    /// </summary>
    public TimeSpan? RetryAfter { get; set; }

    /// <summary>Whether the response never completes (e.g. an infinite response): the request completes when it is aborted.</summary>
    public bool Pending { get; set; }

    /// <summary>Whether the body is streamed without Content-Length (e.g. <c>response.writer.write_content()</c>).</summary>
    public bool Streamed { get; set; }

    /// <summary>
    /// The chunks of the body sent after <see cref="Body"/> (<c>response.writer.write()</c> after a delay), with their delays
    /// measured by the virtual clock: a document is parsed and a media resource is processed as its chunks arrive, and an
    /// event stream (<c>text/event-stream</c>) is interpreted as its chunks arrive. The other resources get the whole body
    /// after the delays.
    /// </summary>
    public List<(TimeSpan Delay, byte[] Data)> Chunks { get; } = [];

    /// <summary>
    /// The condition the <see cref="Chunks"/> wait for (e.g. a value put in the stash by another request), checked every
    /// 100 ms of the virtual clock, or null. When it is still false after <see cref="ChunksTimeout"/>, the handler returns
    /// without sending the chunks: the connection is closed before the end of the body.
    /// </summary>
    public Func<bool>? ChunksCondition { get; set; }

    /// <summary>How long the <see cref="Chunks"/> wait for <see cref="ChunksCondition"/>.</summary>
    public TimeSpan ChunksTimeout { get; set; }

    /// <summary>
    /// The delay after which <see cref="Chunks"/> are sent again, forever (a <c>while True</c> loop that writes and sleeps, e.g.
    /// <c>eventsource/resources/message2.py</c>), or null: the response never ends.
    /// </summary>
    public TimeSpan? ChunksRepeatAfter { get; set; }

    /// <summary>Whether the response is a network error (e.g. an invalid chunked encoding).</summary>
    public bool NetworkError { get; set; }

    /// <summary>Sets the content as a byte string.</summary>
    public string Content
    {
        set => Body = Encoding.Latin1.GetBytes(value);
    }

    /// <summary><c>response.headers.set(name, value)</c></summary>
    public void Set(string name, string? value)
    {
        Headers.RemoveAll(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (value is not null)
        {
            Headers.Add(new(name, value));
        }
    }

    /// <summary><c>response.headers.append(name, value)</c></summary>
    public void Add(string name, string? value)
    {
        if (value is not null)
        {
            Headers.Add(new(name, value));
        }
    }
}
