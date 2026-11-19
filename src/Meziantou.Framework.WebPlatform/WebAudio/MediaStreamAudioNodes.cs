using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebAudio.Rendering;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="MediaStreamAudioSourceNode"/> (https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceOptions"/>
[IdlInterface("MediaStreamAudioSourceOptions", "https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceOptions")]
public sealed class MediaStreamAudioSourceOptions
{
    /// <summary>Gets the stream whose audio is played.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourceoptions-mediastream"/>
    [IdlMember("mediaStream", "https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourceoptions-mediastream")]
    public required MediaStream MediaStream { get; init; }
}

/// <summary>The options of a <see cref="MediaStreamTrackAudioSourceNode"/> (https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceOptions"/>
[IdlInterface("MediaStreamTrackAudioSourceOptions", "https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceOptions")]
public sealed class MediaStreamTrackAudioSourceOptions
{
    /// <summary>Gets the audio track that is played.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamtrackaudiosourceoptions-mediastreamtrack"/>
    [IdlMember("mediaStreamTrack", "https://webaudio.github.io/web-audio-api/#dom-mediastreamtrackaudiosourceoptions-mediastreamtrack")]
    public required MediaStreamTrack MediaStreamTrack { get; init; }
}

/// <summary>The options of a <see cref="MediaElementAudioSourceNode"/> (https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceOptions"/>
[IdlInterface("MediaElementAudioSourceOptions", "https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceOptions")]
public sealed class MediaElementAudioSourceOptions
{
    /// <summary>Gets the media element whose audio is played.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourceoptions-mediaelement"/>
    [IdlMember("mediaElement", "https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourceoptions-mediaelement")]
    public required HtmlMediaElement MediaElement { get; init; }
}

/// <summary>
/// A source whose output is the audio of a track of a <see cref="Dom.MediaStream"/> (https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceNode):
/// the audio track whose identifier is the first in code unit order when the node is created. The track is played even
/// when it is removed from the stream; a track that is ended, disabled or muted outputs silence.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceNode"/>
[IdlInterface("MediaStreamAudioSourceNode", "https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceNode")]
public sealed class MediaStreamAudioSourceNode : AudioNode
{
    private readonly MediaStreamTrackAudioReader _reader;

    /// <summary>Creates a source of the audio of a stream.</summary>
    /// <param name="context">The context.</param>
    /// <param name="options">The options, with the stream.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c>: the stream has no audio track.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourcenode-mediastreamaudiosourcenode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourcenode-mediastreamaudiosourcenode")]
    public MediaStreamAudioSourceNode(AudioContext context, MediaStreamAudioSourceOptions options)
        : base(context, numberOfInputs: 0, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ArgumentNullException.ThrowIfNull(options);
        var tracks = options.MediaStream.GetAudioTracks();
        if (tracks.Count == 0)
            throw DomException.InvalidState("The stream has no audio track");

        var track = tracks.OrderBy(t => t.Id, StringComparer.Ordinal).First();
        MediaStream = options.MediaStream;
        _reader = new MediaStreamTrackAudioReader(track, context.SampleRate);
    }

    /// <summary>Gets the stream given to the constructor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourcenode-mediastream"/>
    [IdlMember("mediaStream", "https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiosourcenode-mediastream")]
    public MediaStream MediaStream { get; }

    // A source whose track ended outputs nothing (https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceNode)
    private protected override bool IsSourceActive(long startFrame, int length) => _reader.IsLive;

    private protected override void Process(long startFrame, int length) => _reader.Read(GetOutputBus(0), length);
}

/// <summary>A source whose output is the audio of a <see cref="MediaStreamTrack"/> (https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceNode"/>
[IdlInterface("MediaStreamTrackAudioSourceNode", "https://webaudio.github.io/web-audio-api/#MediaStreamTrackAudioSourceNode")]
public sealed class MediaStreamTrackAudioSourceNode : AudioNode
{
    private readonly MediaStreamTrackAudioReader _reader;

    /// <summary>Creates a source of the audio of a track.</summary>
    /// <param name="context">The context.</param>
    /// <param name="options">The options, with the track.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c>: the track is not an audio track.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamtrackaudiosourcenode-mediastreamtrackaudiosourcenode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-mediastreamtrackaudiosourcenode-mediastreamtrackaudiosourcenode")]
    public MediaStreamTrackAudioSourceNode(AudioContext context, MediaStreamTrackAudioSourceOptions options)
        : base(context, numberOfInputs: 0, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ArgumentNullException.ThrowIfNull(options);
        var track = options.MediaStreamTrack;
        if (track.Source.Kind != MediaTrackKinds.Audio)
            throw DomException.InvalidState("The track is not an audio track");

        _reader = new MediaStreamTrackAudioReader(track, context.SampleRate);
    }

    // A source whose track ended outputs nothing (https://webaudio.github.io/web-audio-api/#MediaStreamAudioSourceNode)
    private protected override bool IsSourceActive(long startFrame, int length) => _reader.IsLive;

    private protected override void Process(long startFrame, int length) => _reader.Read(GetOutputBus(0), length);
}

/// <summary>
/// A source whose output is the audio of a media element (https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceNode):
/// the audio of the element is no longer played by the element itself. The media backend of the library does not give the
/// decoded audio of the media resources: the output is silence.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceNode"/>
[IdlInterface("MediaElementAudioSourceNode", "https://webaudio.github.io/web-audio-api/#MediaElementAudioSourceNode")]
public sealed class MediaElementAudioSourceNode : AudioNode
{
    /// <summary>Creates a source of the audio of a media element.</summary>
    /// <param name="context">The context.</param>
    /// <param name="options">The options, with the media element.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c>: the element is already the source of a node.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourcenode-mediaelementaudiosourcenode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourcenode-mediaelementaudiosourcenode")]
    public MediaElementAudioSourceNode(AudioContext context, MediaElementAudioSourceOptions options)
        : base(context, numberOfInputs: 0, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MediaElement.AudioSourceNode is not null)
            throw DomException.InvalidState("The media element is already the source of a MediaElementAudioSourceNode");

        options.MediaElement.AudioSourceNode = this;
        MediaElement = options.MediaElement;
    }

    /// <summary>Gets the media element given to the constructor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourcenode-mediaelement"/>
    [IdlMember("mediaElement", "https://webaudio.github.io/web-audio-api/#dom-mediaelementaudiosourcenode-mediaelement")]
    public HtmlMediaElement MediaElement { get; }

    private protected override void Process(long startFrame, int length) => GetOutputBus(0).Zero(1);
}

/// <summary>
/// A destination whose input is the audio of a <see cref="Dom.MediaStream"/> (https://webaudio.github.io/web-audio-api/#MediaStreamAudioDestinationNode):
/// the stream has one audio track, which the other sinks (a media element, another context, <c>MediaStreamTrackProcessor</c>...) can play.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#MediaStreamAudioDestinationNode"/>
[IdlInterface("MediaStreamAudioDestinationNode", "https://webaudio.github.io/web-audio-api/#MediaStreamAudioDestinationNode")]
public sealed class MediaStreamAudioDestinationNode : AudioNode
{
    private readonly WebAudioTrackSource _source;

    /// <summary>Creates a destination and its stream.</summary>
    /// <param name="context">The context.</param>
    /// <param name="options">The options of the node.</param>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiodestinationnode-mediastreamaudiodestinationnode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiodestinationnode-mediastreamaudiodestinationnode")]
    public MediaStreamAudioDestinationNode(AudioContext context, AudioNodeOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 0, channelCount: 2, ChannelCountMode.Explicit, ChannelInterpretation.Speakers)
    {
        ApplyOptions(options);
        _source = new WebAudioTrackSource((int)context.SampleRate, (int)ChannelCount);
        var global = context.Window ?? GlobalScope.Current ?? throw DomException.InvalidState("The context has no window");
        Stream = new MediaStream(global, [new MediaStreamTrack(global, _source)]);
    }

    /// <summary>Gets the stream, with one audio track whose audio is the input of the node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiodestinationnode-stream"/>
    [IdlMember("stream", "https://webaudio.github.io/web-audio-api/#dom-mediastreamaudiodestinationnode-stream")]
    public MediaStream Stream { get; }

    private protected override bool IsSourceActive(long startFrame, int length) => true;

    private protected override void Process(long startFrame, int length) => _source.Write(GetInputBus(0), length);
}

/// <summary>
/// Reads the audio of a track for the render quanta of a context: the frames that follow the ones of the previous quantum,
/// resampled (linear interpolation) when the sample rate of the source is not the one of the context.
/// </summary>
internal sealed class MediaStreamTrackAudioReader(MediaStreamTrack track, float contextSampleRate)
{
    private double _position = -1;
    private float[] _buffer = [];

    /// <summary>Whether the track is live: a source of an ended track is not actively processing.</summary>
    public bool IsLive => track.ReadyState == MediaStreamTrackState.Live;

    public void Read(AudioBus output, int length)
    {
        var source = track.Source;
        var channels = track.AudioChannelCount;
        var step = source.AudioSampleRate / (double)contextSampleRate;
        if (_position < 0)
        {
            // The audio starts with the frames that the source produces now
            _position = Math.Max(0, source.AudioPosition - (long)Math.Ceiling(length * step));
        }

        var first = (long)Math.Floor(_position);
        var frameCount = (int)(Math.Floor(_position + (length * step)) - first) + 2;
        var size = frameCount * channels;
        if (_buffer.Length < size)
        {
            _buffer = new float[size];
        }

        var buffer = _buffer.AsSpan(0, size);
        track.ReadAudio(first, buffer);
        output.SetNumberOfChannels(channels);
        var silent = true;
        for (var channel = 0; channel < channels; channel++)
        {
            var destination = output.GetChannelSpan(channel);
            for (var i = 0; i < length; i++)
            {
                var position = _position + (i * step) - first;
                var index = (int)position;
                var fraction = (float)(position - index);
                var value = buffer[(index * channels) + channel];
                if (fraction != 0)
                {
                    value += (buffer[((index + 1) * channels) + channel] - value) * fraction;
                }

                destination[i] = value;
                silent &= value == 0;
            }
        }

        _position += length * step;
        if (silent)
        {
            output.Zero(channels);
        }
        else
        {
            output.MarkNonSilent();
        }
    }
}

/// <summary>The source of the track of a <see cref="MediaStreamAudioDestinationNode"/>: the frames rendered by the node, kept for one second.</summary>
internal sealed class WebAudioTrackSource : MediaStreamTrackSource
{
    private readonly float[] _frames;
    private readonly int _capacity;
    private readonly int _channelCount;
    private long _written;

    public WebAudioTrackSource(int sampleRate, int channelCount)
        : base(MediaTrackKinds.Audio, "")
    {
        AudioSampleRate = sampleRate;
        _channelCount = Math.Clamp(channelCount, 1, 32);
        _capacity = Math.Max(sampleRate, 128);
        _frames = new float[_capacity * _channelCount];
    }

    public override int AudioSampleRate { get; }

    public override int AudioChannelCount => _channelCount;

    public override long AudioPosition => _written;

    public override Dictionary<string, object> GetInitialSettings() => new(StringComparer.Ordinal)
    {
        ["sampleRate"] = (double)AudioSampleRate,
        ["channelCount"] = (double)_channelCount,
    };

    public void Write(AudioBus input, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var offset = (int)((_written + i) % _capacity) * _channelCount;
            for (var channel = 0; channel < _channelCount; channel++)
            {
                _frames[offset + channel] = input.IsSilent || channel >= input.NumberOfChannels ? 0 : input.GetChannel(channel)[i];
            }
        }

        _written += length;
    }

    public override void ReadAudio(long position, Span<float> destination)
    {
        var frames = destination.Length / _channelCount;
        for (var i = 0; i < frames; i++)
        {
            var frame = position + i;
            var target = destination.Slice(i * _channelCount, _channelCount);
            if (frame < 0 || frame >= _written || frame < _written - _capacity)
            {
                target.Clear();
                continue;
            }

            _frames.AsSpan((int)(frame % _capacity) * _channelCount, _channelCount).CopyTo(target);
        }
    }
}
