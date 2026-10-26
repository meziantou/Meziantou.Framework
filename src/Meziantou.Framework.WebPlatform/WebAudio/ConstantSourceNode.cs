namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of a <see cref="ConstantSourceNode"/> (https://webaudio.github.io/web-audio-api/#ConstantSourceOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ConstantSourceOptions"/>
[IdlInterface("ConstantSourceOptions", "https://webaudio.github.io/web-audio-api/#ConstantSourceOptions")]
public sealed class ConstantSourceOptions
{
    /// <summary>Gets the initial value of <see cref="ConstantSourceNode.Offset"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-constantsourceoptions-offset"/>
    [IdlMember("offset", "https://webaudio.github.io/web-audio-api/#dom-constantsourceoptions-offset")]
    public float Offset { get; init; } = 1;
}

/// <summary>A source whose output is the value of its <see cref="Offset"/> parameter (https://webaudio.github.io/web-audio-api/#ConstantSourceNode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#ConstantSourceNode"/>
[IdlInterface("ConstantSourceNode", "https://webaudio.github.io/web-audio-api/#ConstantSourceNode")]
public sealed class ConstantSourceNode : AudioScheduledSourceNode
{
    /// <summary>Creates a constant source.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-constantsourcenode-constantsourcenode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-constantsourcenode-constantsourcenode")]
    public ConstantSourceNode(BaseAudioContext context, ConstantSourceOptions? options = null)
        : base(context)
    {
        Offset = new AudioParam(context, this, "ConstantSourceNode.offset", 1, -float.MaxValue, float.MaxValue, AutomationRate.ARate);
        if (options is not null)
        {
            Offset.SetInitialValue(options.Offset);
        }
    }

    /// <summary>Gets the value of the output.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-constantsourcenode-offset"/>
    [IdlMember("offset", "https://webaudio.github.io/web-audio-api/#dom-constantsourcenode-offset")]
    public AudioParam Offset { get; }

    private protected override IEnumerable<AudioParam> NodeParameters => [Offset];

    private protected override void Process(long startFrame, int length)
    {
        var output = GetOutputBus(0);
        var (offset, count) = GetPlayingRange(startFrame, length);
        if (count == 0)
        {
            output.Zero(1);
            return;
        }

        output.SetNumberOfChannels(1);
        var destination = output.GetChannelSpan(0);
        destination[..offset].Clear();
        destination[(offset + count)..].Clear();
        if (Offset.IsConstant)
        {
            destination.Slice(offset, count).Fill(Offset.FinalValue);
        }
        else
        {
            Offset.Values.Slice(offset, count).CopyTo(destination[offset..]);
        }

        output.MarkNonSilent();
    }
}
