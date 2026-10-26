using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The final destination of the audio of a context (https://webaudio.github.io/web-audio-api/#AudioDestinationNode): the
/// rendered buffer of an <see cref="OfflineAudioContext"/>, or the (virtual) audio device of an <c>AudioContext</c>.
/// Its output is its input.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioDestinationNode"/>
[IdlInterface("AudioDestinationNode", "https://webaudio.github.io/web-audio-api/#AudioDestinationNode")]
public sealed class AudioDestinationNode : AudioNode
{
    private readonly bool _isOffline;

    internal AudioDestinationNode(BaseAudioContext context, uint channelCount, uint maxChannelCount, bool isOffline)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount, ChannelCountMode.Explicit, ChannelInterpretation.Speakers)
    {
        MaxChannelCount = maxChannelCount;
        _isOffline = isOffline;
    }

    /// <summary>Gets the maximum number of channels of <see cref="AudioNode.ChannelCount"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiodestinationnode-maxchannelcount"/>
    [IdlMember("maxChannelCount", "https://webaudio.github.io/web-audio-api/#dom-audiodestinationnode-maxchannelcount")]
    public uint MaxChannelCount { get; }

    private protected override void ValidateChannelCount(uint value)
    {
        // The channel count of an offline context is the number of channels of its rendered buffer
        if (_isOffline)
        {
            if (value != ChannelCount)
                throw new DomException("The channel count of the destination of an offline context cannot be changed", "NotSupportedError");

            return;
        }

        if (value == 0)
            throw new DomException("The channel count cannot be 0", "NotSupportedError");

        if (value > MaxChannelCount)
            throw new DomException($"The channel count {value} is greater than the maximum channel count {MaxChannelCount}", "IndexSizeError");
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (_isOffline && value != ChannelCountMode.Explicit)
            throw new DomException("The channel count mode of the destination of an offline context cannot be changed", "InvalidStateError");
    }

    // The destination always outputs its channels (silence when nothing is connected)
    private protected override bool IsSourceActive(long startFrame, int length) => true;

    private protected override void Process(long startFrame, int length) => GetOutputBus(0).CopyFrom(GetInputBus(0));
}
