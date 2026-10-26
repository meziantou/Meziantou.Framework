namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The states of an audio context (https://webaudio.github.io/web-audio-api/#enumdef-audiocontextstate).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-audiocontextstate"/>
[IdlInterface("AudioContextState", "https://webaudio.github.io/web-audio-api/#enumdef-audiocontextstate")]
public enum AudioContextState
{
    /// <summary>The context is suspended: its time does not progress.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-suspended"/>
    [IdlMember("suspended", "https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-suspended")]
    Suspended,

    /// <summary>The context is processing audio.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-running"/>
    [IdlMember("running", "https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-running")]
    Running,

    /// <summary>The context is closed: it no longer processes audio.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-closed"/>
    [IdlMember("closed", "https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-closed")]
    Closed,

    /// <summary>The context is interrupted by the user agent (e.g. by another application using the audio device).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-interrupted"/>
    [IdlMember("interrupted", "https://webaudio.github.io/web-audio-api/#dom-audiocontextstate-interrupted")]
    Interrupted,
}

/// <summary>The categories of render quantum sizes (https://webaudio.github.io/web-audio-api/#enumdef-audiocontextrendersizecategory).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-audiocontextrendersizecategory"/>
[IdlInterface("AudioContextRenderSizeCategory", "https://webaudio.github.io/web-audio-api/#enumdef-audiocontextrendersizecategory")]
public enum AudioContextRenderSizeCategory
{
    /// <summary>The default render quantum size (128 frames).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextrendersizecategory-default"/>
    [IdlMember("default", "https://webaudio.github.io/web-audio-api/#dom-audiocontextrendersizecategory-default")]
    Default,

    /// <summary>The render quantum size preferred by the audio device (128 frames: there is no device).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextrendersizecategory-hardware"/>
    [IdlMember("hardware", "https://webaudio.github.io/web-audio-api/#dom-audiocontextrendersizecategory-hardware")]
    Hardware,
}

/// <summary>The categories of latency of an <see cref="AudioContext"/> (https://webaudio.github.io/web-audio-api/#enumdef-audiocontextlatencycategory).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-audiocontextlatencycategory"/>
[IdlInterface("AudioContextLatencyCategory", "https://webaudio.github.io/web-audio-api/#enumdef-audiocontextlatencycategory")]
public enum AudioContextLatencyCategory
{
    /// <summary>A balance between the latency and the power consumption.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-balanced"/>
    [IdlMember("balanced", "https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-balanced")]
    Balanced,

    /// <summary>The lowest latency without glitches (the default).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-interactive"/>
    [IdlMember("interactive", "https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-interactive")]
    Interactive,

    /// <summary>A sustained playback without interruption rather than a low latency.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-playback"/>
    [IdlMember("playback", "https://webaudio.github.io/web-audio-api/#dom-audiocontextlatencycategory-playback")]
    Playback,
}

/// <summary>The types of audio output devices of an <see cref="AudioSinkOptions"/> (https://webaudio.github.io/web-audio-api/#enumdef-audiosinktype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-audiosinktype"/>
[IdlInterface("AudioSinkType", "https://webaudio.github.io/web-audio-api/#enumdef-audiosinktype")]
public enum AudioSinkType
{
    /// <summary>The audio is processed without being played by an output device.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiosinktype-none"/>
    [IdlMember("none", "https://webaudio.github.io/web-audio-api/#dom-audiosinktype-none")]
    None,
}

/// <summary>How the number of channels of an input is computed (https://webaudio.github.io/web-audio-api/#enumdef-channelcountmode).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-channelcountmode"/>
[IdlInterface("ChannelCountMode", "https://webaudio.github.io/web-audio-api/#enumdef-channelcountmode")]
public enum ChannelCountMode
{
    /// <summary>The maximum number of channels of the connections; <see cref="AudioNode.ChannelCount"/> is ignored.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelcountmode-max"/>
    [IdlMember("max", "https://webaudio.github.io/web-audio-api/#dom-channelcountmode-max")]
    Max,

    /// <summary>The maximum number of channels of the connections, clamped to <see cref="AudioNode.ChannelCount"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelcountmode-clamped-max"/>
    [IdlMember("clamped-max", "https://webaudio.github.io/web-audio-api/#dom-channelcountmode-clamped-max")]
    ClampedMax,

    /// <summary>Exactly <see cref="AudioNode.ChannelCount"/> channels.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelcountmode-explicit"/>
    [IdlMember("explicit", "https://webaudio.github.io/web-audio-api/#dom-channelcountmode-explicit")]
    Explicit,
}

/// <summary>How the channels are up-mixed and down-mixed (https://webaudio.github.io/web-audio-api/#enumdef-channelinterpretation).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-channelinterpretation"/>
[IdlInterface("ChannelInterpretation", "https://webaudio.github.io/web-audio-api/#enumdef-channelinterpretation")]
public enum ChannelInterpretation
{
    /// <summary>The channels are speakers (mono, stereo, quad, 5.1) mixed with the speaker rules.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelinterpretation-speakers"/>
    [IdlMember("speakers", "https://webaudio.github.io/web-audio-api/#dom-channelinterpretation-speakers")]
    Speakers,

    /// <summary>The channels are copied in order: the missing ones are silent and the extra ones are dropped.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-channelinterpretation-discrete"/>
    [IdlMember("discrete", "https://webaudio.github.io/web-audio-api/#dom-channelinterpretation-discrete")]
    Discrete,
}

/// <summary>The automation rates of an <see cref="AudioParam"/> (https://webaudio.github.io/web-audio-api/#enumdef-automationrate).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-automationrate"/>
[IdlInterface("AutomationRate", "https://webaudio.github.io/web-audio-api/#enumdef-automationrate")]
public enum AutomationRate
{
    /// <summary>The value is computed for each sample frame.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-automationrate-a-rate"/>
    [IdlMember("a-rate", "https://webaudio.github.io/web-audio-api/#dom-automationrate-a-rate")]
    ARate,

    /// <summary>The value is computed once per render quantum, at its first sample frame.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-automationrate-k-rate"/>
    [IdlMember("k-rate", "https://webaudio.github.io/web-audio-api/#dom-automationrate-k-rate")]
    KRate,
}
