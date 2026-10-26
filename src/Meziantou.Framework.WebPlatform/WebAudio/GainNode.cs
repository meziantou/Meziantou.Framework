namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="GainNode"/> (https://webaudio.github.io/web-audio-api/#GainOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#GainOptions"/>
[IdlInterface("GainOptions", "https://webaudio.github.io/web-audio-api/#GainOptions")]
public sealed class GainOptions : AudioNodeOptions
{
    /// <summary>Gets the initial value of <see cref="GainNode.Gain"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-gainoptions-gain"/>
    [IdlMember("gain", "https://webaudio.github.io/web-audio-api/#dom-gainoptions-gain")]
    public float Gain { get; init; } = 1;
}

/// <summary>A node that multiplies its input by a gain (https://webaudio.github.io/web-audio-api/#GainNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#GainNode"/>
[IdlInterface("GainNode", "https://webaudio.github.io/web-audio-api/#GainNode")]
public sealed class GainNode : AudioNode
{
    /// <summary>Creates a gain node.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-gainnode-gainnode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-gainnode-gainnode")]
    public GainNode(BaseAudioContext context, GainOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.Max, ChannelInterpretation.Speakers)
    {
        Gain = new AudioParam(context, this, "GainNode.gain", 1, -float.MaxValue, float.MaxValue, AutomationRate.ARate);
        ApplyOptions(options);
        if (options is not null)
        {
            Gain.SetInitialValue(options.Gain);
        }
    }

    /// <summary>Gets the gain.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-gainnode-gain"/>
    [IdlMember("gain", "https://webaudio.github.io/web-audio-api/#dom-gainnode-gain")]
    public AudioParam Gain { get; }

    private protected override IEnumerable<AudioParam> Parameters => [Gain];

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        if (input.IsSilent)
        {
            output.Zero(input.NumberOfChannels);
            return;
        }

        output.SetNumberOfChannels(input.NumberOfChannels);
        if (Gain.IsConstant)
        {
            var gain = Gain.FinalValue;
            if (gain == 0)
            {
                output.Zero();
                return;
            }

            for (var channel = 0; channel < input.NumberOfChannels; channel++)
            {
                var source = input.GetChannelSpan(channel);
                var destination = output.GetChannelSpan(channel);
                for (var i = 0; i < source.Length; i++)
                {
                    destination[i] = source[i] * gain;
                }
            }
        }
        else
        {
            var gains = Gain.Values;
            for (var channel = 0; channel < input.NumberOfChannels; channel++)
            {
                var source = input.GetChannelSpan(channel);
                var destination = output.GetChannelSpan(channel);
                for (var i = 0; i < source.Length; i++)
                {
                    destination[i] = source[i] * gains[i];
                }
            }
        }

        output.MarkNonSilent();
    }
}
