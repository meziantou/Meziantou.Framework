using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="ChannelMergerNode"/> (https://webaudio.github.io/web-audio-api/#ChannelMergerOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ChannelMergerOptions"/>
[IdlInterface("ChannelMergerOptions", "https://webaudio.github.io/web-audio-api/#ChannelMergerOptions")]
public sealed class ChannelMergerOptions : AudioNodeOptions
{
    /// <summary>Gets the number of inputs.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelmergeroptions-numberofinputs"/>
    [IdlMember("numberOfInputs", "https://webaudio.github.io/web-audio-api/#dom-channelmergeroptions-numberofinputs")]
    public uint NumberOfInputs { get; init; } = 6;
}

/// <summary>
/// A node that combines its inputs, each down-mixed to mono, into the channels of its output
/// (https://webaudio.github.io/web-audio-api/#ChannelMergerNode).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ChannelMergerNode"/>
[IdlInterface("ChannelMergerNode", "https://webaudio.github.io/web-audio-api/#ChannelMergerNode")]
public sealed class ChannelMergerNode : AudioNode
{
    /// <summary>Creates a channel merger.</summary>
    /// <exception cref="DomException">The number of inputs is not between 1 and 32 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelmergernode-channelmergernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-channelmergernode-channelmergernode")]
    public ChannelMergerNode(BaseAudioContext context, ChannelMergerOptions? options = null)
        : base(context, ValidateNumberOfInputs(options?.NumberOfInputs ?? 6), numberOfOutputs: 1, channelCount: 1, ChannelCountMode.Explicit, ChannelInterpretation.Speakers)
    {
        ApplyOptions(options);
    }

    private static int ValidateNumberOfInputs(uint numberOfInputs)
    {
        if (numberOfInputs is 0 or > MaxChannels)
            throw new DomException($"The number of inputs {numberOfInputs} is not between 1 and {MaxChannels}", "IndexSizeError");

        return (int)numberOfInputs;
    }

    private protected override void ValidateChannelCount(uint value)
    {
        if (value != 1)
            throw new DomException("The channel count of a channel merger cannot be changed", "InvalidStateError");
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value != ChannelCountMode.Explicit)
            throw new DomException("The channel count mode of a channel merger cannot be changed", "InvalidStateError");
    }

    private protected override void Process(long startFrame, int length)
    {
        var output = GetOutputBus(0);
        var inputs = (int)NumberOfInputs;
        output.Zero(inputs);
        var silent = true;
        for (var i = 0; i < inputs; i++)
        {
            var input = GetInputBus(i);
            if (!input.IsSilent)
            {
                input.GetChannelSpan(0).CopyTo(output.GetChannelSpan(i));
                silent = false;
            }
        }

        if (!silent)
        {
            output.MarkNonSilent();
        }
    }
}
