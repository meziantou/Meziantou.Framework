using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="ChannelSplitterNode"/> (https://webaudio.github.io/web-audio-api/#ChannelSplitterOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ChannelSplitterOptions"/>
[IdlInterface("ChannelSplitterOptions", "https://webaudio.github.io/web-audio-api/#ChannelSplitterOptions")]
public sealed class ChannelSplitterOptions : AudioNodeOptions
{
    /// <summary>Gets the number of outputs.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelsplitteroptions-numberofoutputs"/>
    [IdlMember("numberOfOutputs", "https://webaudio.github.io/web-audio-api/#dom-channelsplitteroptions-numberofoutputs")]
    public uint NumberOfOutputs { get; init; } = 6;
}

/// <summary>A node whose outputs are the channels of its input (https://webaudio.github.io/web-audio-api/#ChannelSplitterNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ChannelSplitterNode"/>
[IdlInterface("ChannelSplitterNode", "https://webaudio.github.io/web-audio-api/#ChannelSplitterNode")]
public sealed class ChannelSplitterNode : AudioNode
{
    /// <summary>Creates a channel splitter.</summary>
    /// <exception cref="DomException">The number of outputs is not between 1 and 32 (an <c>IndexSizeError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelsplitternode-channelsplitternode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-channelsplitternode-channelsplitternode")]
    public ChannelSplitterNode(BaseAudioContext context, ChannelSplitterOptions? options = null)
        : base(context, numberOfInputs: 1, ValidateNumberOfOutputs(options?.NumberOfOutputs ?? 6), channelCount: options?.NumberOfOutputs ?? 6, ChannelCountMode.Explicit, ChannelInterpretation.Discrete)
    {
        ApplyOptions(options);
    }

    private static int ValidateNumberOfOutputs(uint numberOfOutputs)
    {
        if (numberOfOutputs is 0 or > MaxChannels)
            throw new DomException($"The number of outputs {numberOfOutputs} is not between 1 and {MaxChannels}", "IndexSizeError");

        return (int)numberOfOutputs;
    }

    private protected override void ValidateChannelCount(uint value)
    {
        if (value != NumberOfOutputs)
            throw new DomException("The channel count of a channel splitter cannot be changed", "InvalidStateError");
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value != ChannelCountMode.Explicit)
            throw new DomException("The channel count mode of a channel splitter cannot be changed", "InvalidStateError");
    }

    private protected override void ValidateChannelInterpretation(ChannelInterpretation value)
    {
        if (value != ChannelInterpretation.Discrete)
            throw new DomException("The channel interpretation of a channel splitter cannot be changed", "InvalidStateError");
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        for (var i = 0; i < (int)NumberOfOutputs; i++)
        {
            var output = GetOutputBus(i);
            if (input.IsSilent || i >= input.NumberOfChannels)
            {
                output.Zero(1);
                continue;
            }

            output.SetNumberOfChannels(1);
            input.GetChannelSpan(i).CopyTo(output.GetChannelSpan(0));
            output.MarkNonSilent();
        }
    }
}
