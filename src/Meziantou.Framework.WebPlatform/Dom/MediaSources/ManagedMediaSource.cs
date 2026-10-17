using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A media source that tells the application when to fetch and append media data
/// (https://w3c.github.io/media-source/#managedmediasource-interface): <see cref="Streaming"/> is true while the source is
/// open and its media element does not have enough data to play without interruption (its ready state is lower than
/// <c>HAVE_ENOUGH_DATA</c>). Its source buffers are <see cref="ManagedSourceBuffer"/> objects.
/// </summary>
/// <remarks>
/// The library never evicts the buffered data of a managed source by itself (the memory cleanup algorithm is only run by a
/// user agent under memory pressure): the data is only evicted when a source buffer is full, as for a
/// <see cref="MediaSource"/>.
/// </remarks>
[IdlInterface("ManagedMediaSource", "https://w3c.github.io/media-source/#managedmediasource-interface")]
public sealed class ManagedMediaSource : MediaSource
{
    private bool _streaming;

    /// <summary>Creates a managed media source (https://w3c.github.io/media-source/#dom-managedmediasource-constructor).</summary>
    [IdlMember("constructor", "https://w3c.github.io/media-source/#dom-managedmediasource-constructor")]
    public ManagedMediaSource()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the application should fetch and append media data
    /// (https://w3c.github.io/media-source/#dom-managedmediasource-streaming).
    /// </summary>
    [IdlMember("streaming", "https://w3c.github.io/media-source/#dom-managedmediasource-streaming")]
    public bool Streaming => _streaming;

    /// <summary>Gets or sets the event handler of the <c>startstreaming</c> event.</summary>
    [IdlMember("onstartstreaming", "https://w3c.github.io/media-source/#dom-managedmediasource-onstartstreaming")]
    public Action<Event>? OnStartStreaming
    {
        get => GetEventHandler("startstreaming");
        set => SetEventHandler("startstreaming", value);
    }

    /// <summary>Gets or sets the event handler of the <c>endstreaming</c> event.</summary>
    [IdlMember("onendstreaming", "https://w3c.github.io/media-source/#dom-managedmediasource-onendstreaming")]
    public Action<Event>? OnEndStreaming
    {
        get => GetEventHandler("endstreaming");
        set => SetEventHandler("endstreaming", value);
    }

    private protected override SourceBuffer CreateSourceBuffer(MediaSegmentParser parser) => new ManagedSourceBuffer(this, parser);

    /// <summary>
    /// https://w3c.github.io/media-source/#managedmediasource-monitoring: the application should append data while the source
    /// is open and the media element cannot play without interruption.
    /// </summary>
    private protected override void OnElementCanPlayUninterruptedChanged(bool canPlayUninterrupted) => SetStreaming(ReadyState == MediaSourceReadyState.Open && !canPlayUninterrupted);

    private protected override void OnReadyStateChanged()
    {
        if (ReadyState != MediaSourceReadyState.Open)
        {
            // The source is detached, or the stream ended: no more data is needed
            SetStreaming(false);
        }
        else if (!ElementCanPlayUninterrupted)
        {
            SetStreaming(true);
        }
    }

    private void SetStreaming(bool value)
    {
        if (_streaming == value)
            return;

        _streaming = value;
        QueueEvent(this, value ? "startstreaming" : "endstreaming");
    }
}

/// <summary>
/// A source buffer of a <see cref="ManagedMediaSource"/> (https://w3c.github.io/media-source/#managedsourcebuffer-interface):
/// a <c>bufferedchange</c> event is fired when its buffered ranges change after <c>appendBuffer()</c>, <c>remove()</c> or
/// <c>endOfStream()</c>.
/// </summary>
[IdlInterface("ManagedSourceBuffer", "https://w3c.github.io/media-source/#managedsourcebuffer-interface")]
public sealed class ManagedSourceBuffer : SourceBuffer
{
    private MediaTimeRange[] _previousRanges = [];

    internal ManagedSourceBuffer(MediaSource parent, MediaSegmentParser parser)
        : base(parent, parser)
    {
    }

    /// <summary>Gets or sets the event handler of the <c>bufferedchange</c> event.</summary>
    [IdlMember("onbufferedchange", "https://w3c.github.io/media-source/#dom-managedsourcebuffer-onbufferedchange")]
    public Action<Event>? OnBufferedChange
    {
        get => GetEventHandler("bufferedchange");
        set => SetEventHandler("bufferedchange", value);
    }

    /// <summary>https://w3c.github.io/media-source/#managedsourcebuffer-buffered-change</summary>
    private protected override void OnBufferedRangesMayHaveChanged(MediaSource parent)
    {
        var ranges = ComputeBufferedRanges(parent.ReadyState == MediaSourceReadyState.Ended);
        if (MediaTimeRanges.AreEqual(ranges, _previousRanges))
            return;

        var added = MediaTimeRanges.Subtract(ranges, _previousRanges);
        var removed = MediaTimeRanges.Subtract(_previousRanges, ranges);
        _previousRanges = ranges;
        parent.QueueTask(() => FireEvent(new BufferedChangeEvent("bufferedchange", new BufferedChangeEventInit { AddedRanges = new TimeRanges(added), RemovedRanges = new TimeRanges(removed) })));
    }
}

/// <summary>https://w3c.github.io/media-source/#dom-bufferedchangeeventinit</summary>
[IdlInterface("BufferedChangeEventInit", "https://w3c.github.io/media-source/#dom-bufferedchangeeventinit")]
public sealed class BufferedChangeEventInit : EventInit
{
    /// <summary>Gets the time ranges that were added.</summary>
    [IdlMember("addedRanges", "https://w3c.github.io/media-source/#dom-bufferedchangeeventinit-addedranges")]
    public TimeRanges? AddedRanges { get; init; }

    /// <summary>Gets the time ranges that were removed.</summary>
    [IdlMember("removedRanges", "https://w3c.github.io/media-source/#dom-bufferedchangeeventinit-removedranges")]
    public TimeRanges? RemovedRanges { get; init; }
}

/// <summary>The event fired when the buffered ranges of a <see cref="ManagedSourceBuffer"/> change (https://w3c.github.io/media-source/#bufferedchangeevent-interface).</summary>
[IdlInterface("BufferedChangeEvent", "https://w3c.github.io/media-source/#bufferedchangeevent-interface")]
public sealed class BufferedChangeEvent : Event
{
    /// <summary>Creates a buffered change event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/media-source/#dom-bufferedchangeevent-constructor")]
    public BufferedChangeEvent(string type, BufferedChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        AddedRanges = eventInitDict?.AddedRanges ?? new TimeRanges();
        RemovedRanges = eventInitDict?.RemovedRanges ?? new TimeRanges();
    }

    /// <summary>Gets the time ranges added since the last <c>updatestart</c> event.</summary>
    [IdlMember("addedRanges", "https://w3c.github.io/media-source/#dom-bufferedchangeevent-addedranges")]
    public TimeRanges AddedRanges { get; }

    /// <summary>Gets the time ranges removed since the last <c>updatestart</c> event.</summary>
    [IdlMember("removedRanges", "https://w3c.github.io/media-source/#dom-bufferedchangeevent-removedranges")]
    public TimeRanges RemovedRanges { get; }
}
