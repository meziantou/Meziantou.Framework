using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The base of the source nodes that are started and stopped at times of the context
/// (https://webaudio.github.io/web-audio-api/#AudioScheduledSourceNode): they output silence before they start and after
/// they stop, and fire <c>ended</c> when they stop.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioScheduledSourceNode"/>
[IdlInterface("AudioScheduledSourceNode", "https://webaudio.github.io/web-audio-api/#AudioScheduledSourceNode")]
public abstract class AudioScheduledSourceNode : AudioNode
{
    private protected AudioScheduledSourceNode(BaseAudioContext context)
        : base(context, numberOfInputs: 0, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
    }

    /// <summary>Gets or sets the event handler of the <c>ended</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-onended"/>
    [IdlMember("onended", "https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-onended")]
    public Action<Event>? OnEnded
    {
        get => GetEventHandler("ended");
        set => SetEventHandler("ended", value);
    }

    /// <summary>[[source started]]: whether <see cref="Start(double)"/> was called.</summary>
    internal bool IsStarted { get; private set; }

    /// <summary>Whether the source stopped playing (its <c>ended</c> event is fired or queued).</summary>
    internal bool HasEnded { get; private set; }

    /// <summary>The start time in seconds.</summary>
    internal double StartTime { get; private set; }

    /// <summary>The stop time in seconds (infinity when <see cref="Stop"/> was not called).</summary>
    internal double StopTime { get; private set; } = double.PositiveInfinity;

    /// <summary>Schedules the start of the playback at a time.</summary>
    /// <exception cref="DomException">The source was already started (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-start"/>
    [IdlMember("start", "https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-start")]
    public void Start(double when = 0)
    {
        StartCore(when);
        MarkStarted(when);
    }

    /// <summary>Validates the arguments of <see cref="Start(double)"/>.</summary>
    private protected void StartCore(double when)
    {
        if (IsStarted)
            throw new DomException("The source was already started", "InvalidStateError");

        if (when < 0)
            throw new RangeErrorException($"The start time {when} cannot be negative");
    }

    /// <summary>Marks the source as started at a time (for the overloads of start that take more arguments).</summary>
    private protected void MarkStarted(double when)
    {
        IsStarted = true;
        StartTime = when;
        OnStarted();
        Context.OnGraphChanged();
        Context.OnSourceStarted();
    }

    /// <summary>Called when the source is started.</summary>
    private protected virtual void OnStarted()
    {
    }

    /// <summary>Schedules the end of the playback at a time.</summary>
    /// <exception cref="DomException">The source was not started (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-stop"/>
    [IdlMember("stop", "https://webaudio.github.io/web-audio-api/#dom-audioscheduledsourcenode-stop")]
    public void Stop(double when = 0)
    {
        if (!IsStarted)
            throw new DomException("The source was not started", "InvalidStateError");

        if (when < 0)
            throw new RangeErrorException($"The stop time {when} cannot be negative");

        if (HasEnded)
            return;

        StopTime = when;
    }

    internal override bool RequiresProcessing => IsStarted && !HasEnded;

    /// <summary>The first frame of the playback (the frame at or after the start time).</summary>
    private protected long StartFrame => FrameAtOrAfter(StartTime);

    /// <summary>The frame at which the playback stops (the frame at or after the stop time, or the end of the time).</summary>
    private protected long StopFrame => double.IsPositiveInfinity(StopTime) ? long.MaxValue : FrameAtOrAfter(StopTime);

    private long FrameAtOrAfter(double time)
    {
        var frame = Math.Ceiling(time * Context.SampleRate);
        return frame >= long.MaxValue ? long.MaxValue : (long)frame;
    }

    private protected override bool IsSourceActive(long startFrame, int length)
    {
        if (!IsStarted || HasEnded)
            return false;

        var start = StartFrame;
        var stop = StopFrame;
        return start < startFrame + length && stop > startFrame && start < stop;
    }

    /// <summary>
    /// Gets the frames of the render quantum during which the source plays: <c>[offset, offset + count)</c> relative to the
    /// start of the quantum. The count is 0 when the source does not play.
    /// </summary>
    private protected (int Offset, int Count) GetPlayingRange(long startFrame, int length)
    {
        var start = Math.Max(StartFrame, startFrame);
        var stop = Math.Min(StopFrame, startFrame + length);
        if (stop <= start)
            return (0, 0);

        return ((int)(start - startFrame), (int)(stop - start));
    }

    /// <summary>Called by the context after each render quantum: the source that reached its stop time ends.</summary>
    internal void CheckEnded(long endFrame)
    {
        if (!IsStarted || HasEnded)
            return;

        if (StopFrame <= endFrame || HasFinishedPlaying)
        {
            Finish();
        }
    }

    /// <summary>Whether the source finished playing before its stop time (e.g. the end of a buffer).</summary>
    private protected virtual bool HasFinishedPlaying => false;

    /// <summary>Ends the playback: the node outputs silence and fires <c>ended</c>.</summary>
    private protected void Finish()
    {
        if (HasEnded)
            return;

        HasEnded = true;
        Context.OnGraphChanged();
        Context.QueueMediaElementTask(() => FireEvent(new Event("ended")));
    }
}
