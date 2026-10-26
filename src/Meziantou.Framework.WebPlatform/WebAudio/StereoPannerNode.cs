using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="StereoPannerNode"/> (https://webaudio.github.io/web-audio-api/#StereoPannerOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#StereoPannerOptions"/>
[IdlInterface("StereoPannerOptions", "https://webaudio.github.io/web-audio-api/#StereoPannerOptions")]
public sealed class StereoPannerOptions : AudioNodeOptions
{
    /// <summary>Gets the initial position, from -1 (left) to 1 (right).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-stereopanneroptions-pan"/>
    [IdlMember("pan", "https://webaudio.github.io/web-audio-api/#dom-stereopanneroptions-pan")]
    public float Pan { get; init; }
}

/// <summary>A node that pans its input in a stereo image with an equal-power law (https://webaudio.github.io/web-audio-api/#StereoPannerNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#StereoPannerNode"/>
[IdlInterface("StereoPannerNode", "https://webaudio.github.io/web-audio-api/#StereoPannerNode")]
public sealed class StereoPannerNode : AudioNode
{
    /// <summary>Creates a stereo panner.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-stereopannernode-stereopannernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-stereopannernode-stereopannernode")]
    public StereoPannerNode(BaseAudioContext context, StereoPannerOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.ClampedMax, ChannelInterpretation.Speakers)
    {
        Pan = new AudioParam(context, this, "StereoPannerNode.pan", 0, -1, 1, AutomationRate.ARate);
        ApplyOptions(options);
        if (options is not null)
        {
            Pan.SetInitialValue(options.Pan);
        }
    }

    /// <summary>Gets the position, from -1 (left) to 1 (right).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-stereopannernode-pan"/>
    [IdlMember("pan", "https://webaudio.github.io/web-audio-api/#dom-stereopannernode-pan")]
    public AudioParam Pan { get; }

    private protected override IEnumerable<AudioParam> Parameters => [Pan];

    private protected override void ValidateChannelCount(uint value)
    {
        if (value > 2)
            throw new DomException($"The channel count of a stereo panner cannot be greater than 2 ({value})", "NotSupportedError");

        base.ValidateChannelCount(value);
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value == ChannelCountMode.Max)
            throw new DomException("The channel count mode of a stereo panner cannot be 'max'", "NotSupportedError");
    }

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        if (input.IsSilent)
        {
            output.Zero(2);
            return;
        }

        output.SetNumberOfChannels(2);
        var left = output.GetChannelSpan(0);
        var right = output.GetChannelSpan(1);
        var mono = input.NumberOfChannels == 1;
        var inputLeft = input.GetChannelSpan(0);
        var inputRight = mono ? inputLeft : input.GetChannelSpan(1);
        var pans = Pan.Values;
        for (var i = 0; i < length; i++)
        {
            // https://webaudio.github.io/web-audio-api/#stereopanner-algorithm
            var pan = Math.Clamp(Pan.IsConstant ? Pan.FinalValue : pans[i], -1.0, 1.0);
            if (mono)
            {
                var x = (pan + 1) / 2;
                var sample = inputLeft[i];
                left[i] = (float)(sample * Math.Cos(x * Math.PI / 2));
                right[i] = (float)(sample * Math.Sin(x * Math.PI / 2));
            }
            else
            {
                var x = pan <= 0 ? pan + 1 : pan;
                var gainLeft = Math.Cos(x * Math.PI / 2);
                var gainRight = Math.Sin(x * Math.PI / 2);
                if (pan <= 0)
                {
                    left[i] = (float)(inputLeft[i] + (inputRight[i] * gainLeft));
                    right[i] = (float)(inputRight[i] * gainRight);
                }
                else
                {
                    left[i] = (float)(inputLeft[i] * gainLeft);
                    right[i] = (float)(inputRight[i] + (inputLeft[i] * gainRight));
                }
            }
        }

        output.MarkNonSilent();
    }
}
