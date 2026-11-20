using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a transferred track, with which the receiving steps create the received track.</summary>
internal sealed record TransferredTrackState(string Id, MediaTrackKinds Kind, string Label, MediaTrackConstraints Constraints, Dictionary<string, object> Settings, bool Enabled, bool Muted, string ContentHint);

/// <summary>A sink of the media pushed by the source of a track (see <see cref="MediaStreamTrackSource.PushesMedia"/>).</summary>
internal interface IMediaStreamTrackSink
{
    /// <summary>A frame of the source: the sink clones it to keep it (the source closes it).</summary>
    void OnVideoFrame(VideoFrame frame);

    /// <summary>Audio samples of the source: the sink clones them to keep them (the source closes them).</summary>
    void OnAudioData(AudioData data);
}

// The sinks of the media pushed by the sources, and the transfer of the tracks (https://w3c.github.io/mediacapture-extensions/#transferable-mediastreamtrack):
// a track posted to a dedicated worker or a window of the same agent cluster
public partial class MediaStreamTrack : IStructuredTransferable
{
    private List<IMediaStreamTrackSink>? _sinks;

    /// <summary>Whether the track was transferred ([[IsDetached]]): it is ended, and it cannot be transferred again.</summary>
    internal bool IsDetached { get; private set; }

    /// <summary>Registers a sink of the media pushed by the source of the track (<see cref="MediaStreamTrackSource.PushesMedia"/>).</summary>
    internal void AddSink(IMediaStreamTrackSink sink) => (_sinks ??= []).Add(sink);

    internal void RemoveSink(IMediaStreamTrackSink sink) => _sinks?.Remove(sink);

    /// <summary>The source pushed a frame: the sinks receive it when the track is live and enabled (a muted source pushes nothing).</summary>
    internal void OnSourceVideoFrame(VideoFrame frame)
    {
        if (_sinks is not { Count: > 0 } sinks || ReadyState == MediaStreamTrackState.Ended || !Enabled)
            return;

        foreach (var sink in sinks.ToArray())
        {
            sink.OnVideoFrame(frame);
        }
    }

    /// <summary>The source pushed audio samples: the sinks receive them when the track is live and enabled.</summary>
    internal void OnSourceAudioData(AudioData data)
    {
        if (_sinks is not { Count: > 0 } sinks || ReadyState == MediaStreamTrackState.Ended || !Enabled)
            return;

        foreach (var sink in sinks.ToArray())
        {
            sink.OnAudioData(data);
        }
    }

    /// <summary>The state of the track given to the track received by a transfer (read on the thread of the track).</summary>
    internal TransferredTrackState GetTransferredState() => new(Id, Source.Kind, Label, _constraints, new Dictionary<string, object>(_settings, StringComparer.Ordinal), _enabled, Muted, _contentHint);

    /// <summary>Creates the track received by a transfer (the transfer-receiving steps), in the global object of the receiver.</summary>
    internal static MediaStreamTrack CreateTransferredTrack(GlobalScope global, MediaStreamTrackSource source, TransferredTrackState state)
    {
        return new MediaStreamTrack(global, source, state.Constraints, new Dictionary<string, object>(state.Settings, StringComparer.Ordinal))
        {
            Id = state.Id,
            _enabled = state.Enabled,
            _contentHint = state.ContentHint,
        };
    }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (IsDetached)
            throw new DomException("The track was transferred", "DataCloneError");
    }

    /// <summary>
    /// The transfer steps: the received track shares the source of the track (through a relay that runs on the event loop of
    /// the source when the track is received by another thread), and the track ends without firing <c>ended</c>.
    /// </summary>
    ITransferredData IStructuredTransferable.Transfer()
    {
        var data = MediaStreamTrackRelay.CreateTransferredData(this);
        IsDetached = true;
        if (ReadyState != MediaStreamTrackState.Ended)
        {
            OnStateChanging();
            ReadyState = MediaStreamTrackState.Ended;
            Source.RemoveTrack(this);
            OnTrackEnded();
        }

        return data;
    }
}
