namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The constrainable properties that the user agent supports (https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints), returned by <see cref="MediaDevices.GetSupportedConstraints"/>.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints"/>
[IdlInterface("MediaTrackSupportedConstraints", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints")]
public sealed class MediaTrackSupportedConstraints
{
    /// <summary>Gets whether the <c>width</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-width"/>
    [IdlMember("width", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-width")]
    public bool Width { get; init; } = true;

    /// <summary>Gets whether the <c>height</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-height"/>
    [IdlMember("height", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-height")]
    public bool Height { get; init; } = true;

    /// <summary>Gets whether the <c>aspectRatio</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-aspectratio"/>
    [IdlMember("aspectRatio", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-aspectratio")]
    public bool AspectRatio { get; init; } = true;

    /// <summary>Gets whether the <c>frameRate</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-framerate"/>
    [IdlMember("frameRate", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-framerate")]
    public bool FrameRate { get; init; } = true;

    /// <summary>Gets whether the <c>facingMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-facingmode"/>
    [IdlMember("facingMode", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-facingmode")]
    public bool FacingMode { get; init; } = true;

    /// <summary>Gets whether the <c>resizeMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-resizemode"/>
    [IdlMember("resizeMode", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-resizemode")]
    public bool ResizeMode { get; init; } = true;

    /// <summary>Gets whether the <c>sampleRate</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-samplerate")]
    public bool SampleRate { get; init; } = true;

    /// <summary>Gets whether the <c>sampleSize</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-samplesize"/>
    [IdlMember("sampleSize", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-samplesize")]
    public bool SampleSize { get; init; } = true;

    /// <summary>Gets whether the <c>echoCancellation</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-echocancellation"/>
    [IdlMember("echoCancellation", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-echocancellation")]
    public bool EchoCancellation { get; init; } = true;

    /// <summary>Gets whether the <c>autoGainControl</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-autogaincontrol"/>
    [IdlMember("autoGainControl", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-autogaincontrol")]
    public bool AutoGainControl { get; init; } = true;

    /// <summary>Gets whether the <c>noiseSuppression</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-noisesuppression"/>
    [IdlMember("noiseSuppression", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-noisesuppression")]
    public bool NoiseSuppression { get; init; } = true;

    /// <summary>Gets whether the <c>latency</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-latency"/>
    [IdlMember("latency", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-latency")]
    public bool Latency { get; init; } = true;

    /// <summary>Gets whether the <c>channelCount</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-channelcount"/>
    [IdlMember("channelCount", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-channelcount")]
    public bool ChannelCount { get; init; } = true;

    /// <summary>Gets whether the <c>deviceId</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-deviceid")]
    public bool DeviceId { get; init; } = true;

    /// <summary>Gets whether the <c>groupId</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-groupid"/>
    [IdlMember("groupId", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-groupid")]
    public bool GroupId { get; init; } = true;

    /// <summary>Gets whether the <c>backgroundBlur</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-backgroundblur"/>
    [IdlMember("backgroundBlur", "https://w3c.github.io/mediacapture-main/#dom-mediatracksupportedconstraints-backgroundblur")]
    public bool BackgroundBlur { get; init; } = true;

    /// <summary>Gets whether the <c>displaySurface</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-displaysurface"/>
    [IdlMember("displaySurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-displaysurface")]
    public bool DisplaySurface { get; init; } = true;

    /// <summary>Gets whether the <c>logicalSurface</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-logicalsurface"/>
    [IdlMember("logicalSurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-logicalsurface")]
    public bool LogicalSurface { get; init; } = true;

    /// <summary>Gets whether the <c>cursor</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-cursor"/>
    [IdlMember("cursor", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-cursor")]
    public bool Cursor { get; init; } = true;

    /// <summary>Gets whether the <c>restrictOwnAudio</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-restrictownaudio"/>
    [IdlMember("restrictOwnAudio", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-restrictownaudio")]
    public bool RestrictOwnAudio { get; init; } = true;

    /// <summary>Gets whether the <c>suppressLocalAudioPlayback</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-suppresslocalaudioplayback"/>
    [IdlMember("suppressLocalAudioPlayback", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksupportedconstraints-suppresslocalaudioplayback")]
    public bool SuppressLocalAudioPlayback { get; init; } = true;

    /// <summary>Gets whether the <c>whiteBalanceMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-whitebalancemode"/>
    [IdlMember("whiteBalanceMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-whitebalancemode")]
    public bool WhiteBalanceMode { get; init; } = true;

    /// <summary>Gets whether the <c>exposureMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposuremode"/>
    [IdlMember("exposureMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposuremode")]
    public bool ExposureMode { get; init; } = true;

    /// <summary>Gets whether the <c>focusMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-focusmode"/>
    [IdlMember("focusMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-focusmode")]
    public bool FocusMode { get; init; } = true;

    /// <summary>Gets whether the <c>pointsOfInterest</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-pointsofinterest"/>
    [IdlMember("pointsOfInterest", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-pointsofinterest")]
    public bool PointsOfInterest { get; init; } = true;

    /// <summary>Gets whether the <c>exposureCompensation</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposurecompensation"/>
    [IdlMember("exposureCompensation", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposurecompensation")]
    public bool ExposureCompensation { get; init; } = true;

    /// <summary>Gets whether the <c>exposureTime</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposuretime"/>
    [IdlMember("exposureTime", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-exposuretime")]
    public bool ExposureTime { get; init; } = true;

    /// <summary>Gets whether the <c>colorTemperature</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-colortemperature"/>
    [IdlMember("colorTemperature", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-colortemperature")]
    public bool ColorTemperature { get; init; } = true;

    /// <summary>Gets whether the <c>iso</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-iso"/>
    [IdlMember("iso", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-iso")]
    public bool Iso { get; init; } = true;

    /// <summary>Gets whether the <c>brightness</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-brightness"/>
    [IdlMember("brightness", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-brightness")]
    public bool Brightness { get; init; } = true;

    /// <summary>Gets whether the <c>contrast</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-contrast"/>
    [IdlMember("contrast", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-contrast")]
    public bool Contrast { get; init; } = true;

    /// <summary>Gets whether the <c>pan</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-pan"/>
    [IdlMember("pan", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-pan")]
    public bool Pan { get; init; } = true;

    /// <summary>Gets whether the <c>saturation</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-saturation"/>
    [IdlMember("saturation", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-saturation")]
    public bool Saturation { get; init; } = true;

    /// <summary>Gets whether the <c>sharpness</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-sharpness"/>
    [IdlMember("sharpness", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-sharpness")]
    public bool Sharpness { get; init; } = true;

    /// <summary>Gets whether the <c>focusDistance</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-focusdistance"/>
    [IdlMember("focusDistance", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-focusdistance")]
    public bool FocusDistance { get; init; } = true;

    /// <summary>Gets whether the <c>tilt</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-tilt"/>
    [IdlMember("tilt", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-tilt")]
    public bool Tilt { get; init; } = true;

    /// <summary>Gets whether the <c>zoom</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-zoom"/>
    [IdlMember("zoom", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-zoom")]
    public bool Zoom { get; init; } = true;

    /// <summary>Gets whether the <c>torch</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-torch"/>
    [IdlMember("torch", "https://w3c.github.io/mediacapture-image/#dom-mediatracksupportedconstraints-torch")]
    public bool Torch { get; init; } = true;

    /// <summary>Gets whether the <c>powerEfficient</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-powerefficient"/>
    [IdlMember("powerEfficient", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-powerefficient")]
    public bool PowerEfficient { get; init; } = true;

    /// <summary>Gets whether the <c>powerEfficientPixelFormat</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-powerefficientpixelformat"/>
    [IdlMember("powerEfficientPixelFormat", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-powerefficientpixelformat")]
    public bool PowerEfficientPixelFormat { get; init; } = true;

    /// <summary>Gets whether the <c>gestureReactions</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-gesturereactions"/>
    [IdlMember("gestureReactions", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-gesturereactions")]
    public bool GestureReactions { get; init; } = true;

    /// <summary>Gets whether the <c>faceFraming</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-faceframing"/>
    [IdlMember("faceFraming", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-faceframing")]
    public bool FaceFraming { get; init; } = true;

    /// <summary>Gets whether the <c>eyeGazeCorrection</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-eyegazecorrection"/>
    [IdlMember("eyeGazeCorrection", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-eyegazecorrection")]
    public bool EyeGazeCorrection { get; init; } = true;

    /// <summary>Gets whether the <c>voiceIsolation</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-voiceisolation"/>
    [IdlMember("voiceIsolation", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-voiceisolation")]
    public bool VoiceIsolation { get; init; } = true;

    /// <summary>Gets whether the <c>humanFaceDetectionMode</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-humanfacedetectionmode"/>
    [IdlMember("humanFaceDetectionMode", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-humanfacedetectionmode")]
    public bool HumanFaceDetectionMode { get; init; } = true;

    /// <summary>Gets whether the <c>backgroundSegmentationMask</c> constrainable property is supported.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-backgroundsegmentationmask"/>
    [IdlMember("backgroundSegmentationMask", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksupportedconstraints-backgroundsegmentationmask")]
    public bool BackgroundSegmentationMask { get; init; } = true;
}

/// <summary>The capabilities of the source of a track (https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities): the values or the ranges of values of its constrainable properties, returned by <see cref="MediaStreamTrack.GetCapabilities"/> and <see cref="InputDeviceInfo.GetCapabilities"/>.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities"/>
[IdlInterface("MediaTrackCapabilities", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities")]
public sealed class MediaTrackCapabilities
{
    /// <summary>Gets the values or the range of values of the <c>width</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-width"/>
    [IdlMember("width", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-width")]
    public ULongRange? Width { get; init; }

    /// <summary>Gets the values or the range of values of the <c>height</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-height"/>
    [IdlMember("height", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-height")]
    public ULongRange? Height { get; init; }

    /// <summary>Gets the values or the range of values of the <c>aspectRatio</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-aspectratio"/>
    [IdlMember("aspectRatio", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-aspectratio")]
    public DoubleRange? AspectRatio { get; init; }

    /// <summary>Gets the values or the range of values of the <c>frameRate</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-framerate"/>
    [IdlMember("frameRate", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-framerate")]
    public DoubleRange? FrameRate { get; init; }

    /// <summary>Gets the values or the range of values of the <c>facingMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-facingmode"/>
    [IdlMember("facingMode", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-facingmode")]
    public IReadOnlyList<string>? FacingMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>resizeMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-resizemode"/>
    [IdlMember("resizeMode", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-resizemode")]
    public IReadOnlyList<string>? ResizeMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>sampleRate</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-samplerate")]
    public ULongRange? SampleRate { get; init; }

    /// <summary>Gets the values or the range of values of the <c>sampleSize</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-samplesize"/>
    [IdlMember("sampleSize", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-samplesize")]
    public ULongRange? SampleSize { get; init; }

    /// <summary>Gets the values or the range of values of the <c>echoCancellation</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-echocancellation"/>
    [IdlMember("echoCancellation", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-echocancellation")]
    public IReadOnlyList<BooleanOrString>? EchoCancellation { get; init; }

    /// <summary>Gets the values or the range of values of the <c>autoGainControl</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-autogaincontrol"/>
    [IdlMember("autoGainControl", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-autogaincontrol")]
    public IReadOnlyList<bool>? AutoGainControl { get; init; }

    /// <summary>Gets the values or the range of values of the <c>noiseSuppression</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-noisesuppression"/>
    [IdlMember("noiseSuppression", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-noisesuppression")]
    public IReadOnlyList<bool>? NoiseSuppression { get; init; }

    /// <summary>Gets the values or the range of values of the <c>latency</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-latency"/>
    [IdlMember("latency", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-latency")]
    public DoubleRange? Latency { get; init; }

    /// <summary>Gets the values or the range of values of the <c>channelCount</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-channelcount"/>
    [IdlMember("channelCount", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-channelcount")]
    public ULongRange? ChannelCount { get; init; }

    /// <summary>Gets the values or the range of values of the <c>deviceId</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-deviceid")]
    public string? DeviceId { get; init; }

    /// <summary>Gets the values or the range of values of the <c>groupId</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-groupid"/>
    [IdlMember("groupId", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-groupid")]
    public string? GroupId { get; init; }

    /// <summary>Gets the values or the range of values of the <c>backgroundBlur</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-backgroundblur"/>
    [IdlMember("backgroundBlur", "https://w3c.github.io/mediacapture-main/#dom-mediatrackcapabilities-backgroundblur")]
    public IReadOnlyList<bool>? BackgroundBlur { get; init; }

    /// <summary>Gets the values or the range of values of the <c>displaySurface</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-displaysurface"/>
    [IdlMember("displaySurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-displaysurface")]
    public string? DisplaySurface { get; init; }

    /// <summary>Gets the values or the range of values of the <c>logicalSurface</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-logicalsurface"/>
    [IdlMember("logicalSurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-logicalsurface")]
    public bool? LogicalSurface { get; init; }

    /// <summary>Gets the values or the range of values of the <c>cursor</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-cursor"/>
    [IdlMember("cursor", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackcapabilities-cursor")]
    public IReadOnlyList<string>? Cursor { get; init; }

    /// <summary>Gets the values or the range of values of the <c>whiteBalanceMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-whitebalancemode"/>
    [IdlMember("whiteBalanceMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-whitebalancemode")]
    public IReadOnlyList<string>? WhiteBalanceMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>exposureMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposuremode"/>
    [IdlMember("exposureMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposuremode")]
    public IReadOnlyList<string>? ExposureMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>focusMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-focusmode"/>
    [IdlMember("focusMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-focusmode")]
    public IReadOnlyList<string>? FocusMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>exposureCompensation</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposurecompensation"/>
    [IdlMember("exposureCompensation", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposurecompensation")]
    public MediaSettingsRange? ExposureCompensation { get; init; }

    /// <summary>Gets the values or the range of values of the <c>exposureTime</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposuretime"/>
    [IdlMember("exposureTime", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-exposuretime")]
    public MediaSettingsRange? ExposureTime { get; init; }

    /// <summary>Gets the values or the range of values of the <c>colorTemperature</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-colortemperature"/>
    [IdlMember("colorTemperature", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-colortemperature")]
    public MediaSettingsRange? ColorTemperature { get; init; }

    /// <summary>Gets the values or the range of values of the <c>iso</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-iso"/>
    [IdlMember("iso", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-iso")]
    public MediaSettingsRange? Iso { get; init; }

    /// <summary>Gets the values or the range of values of the <c>brightness</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-brightness"/>
    [IdlMember("brightness", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-brightness")]
    public MediaSettingsRange? Brightness { get; init; }

    /// <summary>Gets the values or the range of values of the <c>contrast</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-contrast"/>
    [IdlMember("contrast", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-contrast")]
    public MediaSettingsRange? Contrast { get; init; }

    /// <summary>Gets the values or the range of values of the <c>saturation</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-saturation"/>
    [IdlMember("saturation", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-saturation")]
    public MediaSettingsRange? Saturation { get; init; }

    /// <summary>Gets the values or the range of values of the <c>sharpness</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-sharpness"/>
    [IdlMember("sharpness", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-sharpness")]
    public MediaSettingsRange? Sharpness { get; init; }

    /// <summary>Gets the values or the range of values of the <c>focusDistance</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-focusdistance"/>
    [IdlMember("focusDistance", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-focusdistance")]
    public MediaSettingsRange? FocusDistance { get; init; }

    /// <summary>Gets the values or the range of values of the <c>pan</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-pan"/>
    [IdlMember("pan", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-pan")]
    public MediaSettingsRange? Pan { get; init; }

    /// <summary>Gets the values or the range of values of the <c>tilt</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-tilt"/>
    [IdlMember("tilt", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-tilt")]
    public MediaSettingsRange? Tilt { get; init; }

    /// <summary>Gets the values or the range of values of the <c>zoom</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-zoom"/>
    [IdlMember("zoom", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-zoom")]
    public MediaSettingsRange? Zoom { get; init; }

    /// <summary>Gets the values or the range of values of the <c>torch</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-torch"/>
    [IdlMember("torch", "https://w3c.github.io/mediacapture-image/#dom-mediatrackcapabilities-torch")]
    public IReadOnlyList<bool>? Torch { get; init; }

    /// <summary>Gets the values or the range of values of the <c>powerEfficient</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-powerefficient"/>
    [IdlMember("powerEfficient", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-powerefficient")]
    public IReadOnlyList<bool>? PowerEfficient { get; init; }

    /// <summary>Gets the values or the range of values of the <c>powerEfficientPixelFormat</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-powerefficientpixelformat"/>
    [IdlMember("powerEfficientPixelFormat", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-powerefficientpixelformat")]
    public IReadOnlyList<bool>? PowerEfficientPixelFormat { get; init; }

    /// <summary>Gets the values or the range of values of the <c>gestureReactions</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-gesturereactions"/>
    [IdlMember("gestureReactions", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-gesturereactions")]
    public IReadOnlyList<bool>? GestureReactions { get; init; }

    /// <summary>Gets the values or the range of values of the <c>faceFraming</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-faceframing"/>
    [IdlMember("faceFraming", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-faceframing")]
    public IReadOnlyList<bool>? FaceFraming { get; init; }

    /// <summary>Gets the values or the range of values of the <c>eyeGazeCorrection</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-eyegazecorrection"/>
    [IdlMember("eyeGazeCorrection", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-eyegazecorrection")]
    public IReadOnlyList<bool>? EyeGazeCorrection { get; init; }

    /// <summary>Gets the values or the range of values of the <c>voiceIsolation</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-voiceisolation"/>
    [IdlMember("voiceIsolation", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-voiceisolation")]
    public IReadOnlyList<bool>? VoiceIsolation { get; init; }

    /// <summary>Gets the values or the range of values of the <c>humanFaceDetectionMode</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-humanfacedetectionmode"/>
    [IdlMember("humanFaceDetectionMode", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-humanfacedetectionmode")]
    public IReadOnlyList<string>? HumanFaceDetectionMode { get; init; }

    /// <summary>Gets the values or the range of values of the <c>backgroundSegmentationMask</c> constrainable property that the source supports, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-backgroundsegmentationmask"/>
    [IdlMember("backgroundSegmentationMask", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackcapabilities-backgroundsegmentationmask")]
    public IReadOnlyList<bool>? BackgroundSegmentationMask { get; init; }
}

/// <summary>A set of constraints on the constrainable properties of a track (https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset"/>
[IdlInterface("MediaTrackConstraintSet", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset")]
public class MediaTrackConstraintSet
{
    /// <summary>Gets the constraint on the <c>width</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-width"/>
    [IdlMember("width", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-width")]
    public ConstrainULong? Width { get; init; }

    /// <summary>Gets the constraint on the <c>height</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-height"/>
    [IdlMember("height", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-height")]
    public ConstrainULong? Height { get; init; }

    /// <summary>Gets the constraint on the <c>aspectRatio</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-aspectratio"/>
    [IdlMember("aspectRatio", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-aspectratio")]
    public ConstrainDouble? AspectRatio { get; init; }

    /// <summary>Gets the constraint on the <c>frameRate</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-framerate"/>
    [IdlMember("frameRate", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-framerate")]
    public ConstrainDouble? FrameRate { get; init; }

    /// <summary>Gets the constraint on the <c>facingMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-facingmode"/>
    [IdlMember("facingMode", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-facingmode")]
    public ConstrainDomString? FacingMode { get; init; }

    /// <summary>Gets the constraint on the <c>resizeMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-resizemode"/>
    [IdlMember("resizeMode", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-resizemode")]
    public ConstrainDomString? ResizeMode { get; init; }

    /// <summary>Gets the constraint on the <c>sampleRate</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-samplerate")]
    public ConstrainULong? SampleRate { get; init; }

    /// <summary>Gets the constraint on the <c>sampleSize</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-samplesize"/>
    [IdlMember("sampleSize", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-samplesize")]
    public ConstrainULong? SampleSize { get; init; }

    /// <summary>Gets the constraint on the <c>echoCancellation</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-echocancellation"/>
    [IdlMember("echoCancellation", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-echocancellation")]
    public ConstrainBooleanOrDomString? EchoCancellation { get; init; }

    /// <summary>Gets the constraint on the <c>autoGainControl</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-autogaincontrol"/>
    [IdlMember("autoGainControl", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-autogaincontrol")]
    public ConstrainBoolean? AutoGainControl { get; init; }

    /// <summary>Gets the constraint on the <c>noiseSuppression</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-noisesuppression"/>
    [IdlMember("noiseSuppression", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-noisesuppression")]
    public ConstrainBoolean? NoiseSuppression { get; init; }

    /// <summary>Gets the constraint on the <c>latency</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-latency"/>
    [IdlMember("latency", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-latency")]
    public ConstrainDouble? Latency { get; init; }

    /// <summary>Gets the constraint on the <c>channelCount</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-channelcount"/>
    [IdlMember("channelCount", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-channelcount")]
    public ConstrainULong? ChannelCount { get; init; }

    /// <summary>Gets the constraint on the <c>deviceId</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-deviceid")]
    public ConstrainDomString? DeviceId { get; init; }

    /// <summary>Gets the constraint on the <c>groupId</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-groupid"/>
    [IdlMember("groupId", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-groupid")]
    public ConstrainDomString? GroupId { get; init; }

    /// <summary>Gets the constraint on the <c>backgroundBlur</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-backgroundblur"/>
    [IdlMember("backgroundBlur", "https://w3c.github.io/mediacapture-main/#dom-mediatrackconstraintset-backgroundblur")]
    public ConstrainBoolean? BackgroundBlur { get; init; }

    /// <summary>Gets the constraint on the <c>displaySurface</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-displaysurface"/>
    [IdlMember("displaySurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-displaysurface")]
    public ConstrainDomString? DisplaySurface { get; init; }

    /// <summary>Gets the constraint on the <c>logicalSurface</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-logicalsurface"/>
    [IdlMember("logicalSurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-logicalsurface")]
    public ConstrainBoolean? LogicalSurface { get; init; }

    /// <summary>Gets the constraint on the <c>cursor</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-cursor"/>
    [IdlMember("cursor", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-cursor")]
    public ConstrainDomString? Cursor { get; init; }

    /// <summary>Gets the constraint on the <c>restrictOwnAudio</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-restrictownaudio"/>
    [IdlMember("restrictOwnAudio", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-restrictownaudio")]
    public ConstrainBoolean? RestrictOwnAudio { get; init; }

    /// <summary>Gets the constraint on the <c>suppressLocalAudioPlayback</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-suppresslocalaudioplayback"/>
    [IdlMember("suppressLocalAudioPlayback", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatrackconstraintset-suppresslocalaudioplayback")]
    public ConstrainBoolean? SuppressLocalAudioPlayback { get; init; }

    /// <summary>Gets the constraint on the <c>whiteBalanceMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-whitebalancemode"/>
    [IdlMember("whiteBalanceMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-whitebalancemode")]
    public ConstrainDomString? WhiteBalanceMode { get; init; }

    /// <summary>Gets the constraint on the <c>exposureMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposuremode"/>
    [IdlMember("exposureMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposuremode")]
    public ConstrainDomString? ExposureMode { get; init; }

    /// <summary>Gets the constraint on the <c>focusMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-focusmode"/>
    [IdlMember("focusMode", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-focusmode")]
    public ConstrainDomString? FocusMode { get; init; }

    /// <summary>Gets the constraint on the <c>pointsOfInterest</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-pointsofinterest"/>
    [IdlMember("pointsOfInterest", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-pointsofinterest")]
    public ConstrainPoint2D? PointsOfInterest { get; init; }

    /// <summary>Gets the constraint on the <c>exposureCompensation</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposurecompensation"/>
    [IdlMember("exposureCompensation", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposurecompensation")]
    public ConstrainDouble? ExposureCompensation { get; init; }

    /// <summary>Gets the constraint on the <c>exposureTime</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposuretime"/>
    [IdlMember("exposureTime", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-exposuretime")]
    public ConstrainDouble? ExposureTime { get; init; }

    /// <summary>Gets the constraint on the <c>colorTemperature</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-colortemperature"/>
    [IdlMember("colorTemperature", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-colortemperature")]
    public ConstrainDouble? ColorTemperature { get; init; }

    /// <summary>Gets the constraint on the <c>iso</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-iso"/>
    [IdlMember("iso", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-iso")]
    public ConstrainDouble? Iso { get; init; }

    /// <summary>Gets the constraint on the <c>brightness</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-brightness"/>
    [IdlMember("brightness", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-brightness")]
    public ConstrainDouble? Brightness { get; init; }

    /// <summary>Gets the constraint on the <c>contrast</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-contrast"/>
    [IdlMember("contrast", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-contrast")]
    public ConstrainDouble? Contrast { get; init; }

    /// <summary>Gets the constraint on the <c>saturation</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-saturation"/>
    [IdlMember("saturation", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-saturation")]
    public ConstrainDouble? Saturation { get; init; }

    /// <summary>Gets the constraint on the <c>sharpness</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-sharpness"/>
    [IdlMember("sharpness", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-sharpness")]
    public ConstrainDouble? Sharpness { get; init; }

    /// <summary>Gets the constraint on the <c>focusDistance</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-focusdistance"/>
    [IdlMember("focusDistance", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-focusdistance")]
    public ConstrainDouble? FocusDistance { get; init; }

    /// <summary>Gets the constraint on the <c>pan</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-pan"/>
    [IdlMember("pan", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-pan")]
    public BooleanOrConstrainDouble? Pan { get; init; }

    /// <summary>Gets the constraint on the <c>tilt</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-tilt"/>
    [IdlMember("tilt", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-tilt")]
    public BooleanOrConstrainDouble? Tilt { get; init; }

    /// <summary>Gets the constraint on the <c>zoom</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-zoom"/>
    [IdlMember("zoom", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-zoom")]
    public BooleanOrConstrainDouble? Zoom { get; init; }

    /// <summary>Gets the constraint on the <c>torch</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-torch"/>
    [IdlMember("torch", "https://w3c.github.io/mediacapture-image/#dom-mediatrackconstraintset-torch")]
    public ConstrainBoolean? Torch { get; init; }

    /// <summary>Gets the constraint on the <c>gestureReactions</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-gesturereactions"/>
    [IdlMember("gestureReactions", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-gesturereactions")]
    public ConstrainBoolean? GestureReactions { get; init; }

    /// <summary>Gets the constraint on the <c>faceFraming</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-faceframing"/>
    [IdlMember("faceFraming", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-faceframing")]
    public ConstrainBoolean? FaceFraming { get; init; }

    /// <summary>Gets the constraint on the <c>eyeGazeCorrection</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-eyegazecorrection"/>
    [IdlMember("eyeGazeCorrection", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-eyegazecorrection")]
    public ConstrainBoolean? EyeGazeCorrection { get; init; }

    /// <summary>Gets the constraint on the <c>voiceIsolation</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-voiceisolation"/>
    [IdlMember("voiceIsolation", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-voiceisolation")]
    public ConstrainBoolean? VoiceIsolation { get; init; }

    /// <summary>Gets the constraint on the <c>humanFaceDetectionMode</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-humanfacedetectionmode"/>
    [IdlMember("humanFaceDetectionMode", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-humanfacedetectionmode")]
    public ConstrainDomString? HumanFaceDetectionMode { get; init; }

    /// <summary>Gets the constraint on the <c>backgroundSegmentationMask</c> constrainable property, or null.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-backgroundsegmentationmask"/>
    [IdlMember("backgroundSegmentationMask", "https://w3c.github.io/mediacapture-extensions/#dom-mediatrackconstraintset-backgroundsegmentationmask")]
    public ConstrainBoolean? BackgroundSegmentationMask { get; init; }
}

/// <summary>The current settings of the constrainable properties of a track (https://w3c.github.io/mediacapture-main/#dom-mediatracksettings), returned by <see cref="MediaStreamTrack.GetSettings"/>.</summary>
/// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings"/>
[IdlInterface("MediaTrackSettings", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings")]
public sealed class MediaTrackSettings
{
    /// <summary>Gets the current value of the <c>width</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-width"/>
    [IdlMember("width", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-width")]
    public uint? Width { get; init; }

    /// <summary>Gets the current value of the <c>height</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-height"/>
    [IdlMember("height", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-height")]
    public uint? Height { get; init; }

    /// <summary>Gets the current value of the <c>aspectRatio</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-aspectratio"/>
    [IdlMember("aspectRatio", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-aspectratio")]
    public double? AspectRatio { get; init; }

    /// <summary>Gets the current value of the <c>frameRate</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-framerate"/>
    [IdlMember("frameRate", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-framerate")]
    public double? FrameRate { get; init; }

    /// <summary>Gets the current value of the <c>facingMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-facingmode"/>
    [IdlMember("facingMode", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-facingmode")]
    public string? FacingMode { get; init; }

    /// <summary>Gets the current value of the <c>resizeMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-resizemode"/>
    [IdlMember("resizeMode", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-resizemode")]
    public string? ResizeMode { get; init; }

    /// <summary>Gets the current value of the <c>sampleRate</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-samplerate"/>
    [IdlMember("sampleRate", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-samplerate")]
    public uint? SampleRate { get; init; }

    /// <summary>Gets the current value of the <c>sampleSize</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-samplesize"/>
    [IdlMember("sampleSize", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-samplesize")]
    public uint? SampleSize { get; init; }

    /// <summary>Gets the current value of the <c>echoCancellation</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-echocancellation"/>
    [IdlMember("echoCancellation", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-echocancellation")]
    public BooleanOrString? EchoCancellation { get; init; }

    /// <summary>Gets the current value of the <c>autoGainControl</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-autogaincontrol"/>
    [IdlMember("autoGainControl", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-autogaincontrol")]
    public bool? AutoGainControl { get; init; }

    /// <summary>Gets the current value of the <c>noiseSuppression</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-noisesuppression"/>
    [IdlMember("noiseSuppression", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-noisesuppression")]
    public bool? NoiseSuppression { get; init; }

    /// <summary>Gets the current value of the <c>latency</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-latency"/>
    [IdlMember("latency", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-latency")]
    public double? Latency { get; init; }

    /// <summary>Gets the current value of the <c>channelCount</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-channelcount"/>
    [IdlMember("channelCount", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-channelcount")]
    public uint? ChannelCount { get; init; }

    /// <summary>Gets the current value of the <c>deviceId</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-deviceid"/>
    [IdlMember("deviceId", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-deviceid")]
    public string? DeviceId { get; init; }

    /// <summary>Gets the current value of the <c>groupId</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-groupid"/>
    [IdlMember("groupId", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-groupid")]
    public string? GroupId { get; init; }

    /// <summary>Gets the current value of the <c>backgroundBlur</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-backgroundblur"/>
    [IdlMember("backgroundBlur", "https://w3c.github.io/mediacapture-main/#dom-mediatracksettings-backgroundblur")]
    public bool? BackgroundBlur { get; init; }

    /// <summary>Gets the current value of the <c>displaySurface</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-displaysurface"/>
    [IdlMember("displaySurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-displaysurface")]
    public string? DisplaySurface { get; init; }

    /// <summary>Gets the current value of the <c>logicalSurface</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-logicalsurface"/>
    [IdlMember("logicalSurface", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-logicalsurface")]
    public bool? LogicalSurface { get; init; }

    /// <summary>Gets the current value of the <c>cursor</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-cursor"/>
    [IdlMember("cursor", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-cursor")]
    public string? Cursor { get; init; }

    /// <summary>Gets the current value of the <c>restrictOwnAudio</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-restrictownaudio"/>
    [IdlMember("restrictOwnAudio", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-restrictownaudio")]
    public bool? RestrictOwnAudio { get; init; }

    /// <summary>Gets the current value of the <c>suppressLocalAudioPlayback</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-suppresslocalaudioplayback"/>
    [IdlMember("suppressLocalAudioPlayback", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-suppresslocalaudioplayback")]
    public bool? SuppressLocalAudioPlayback { get; init; }

    /// <summary>Gets the current value of the <c>screenPixelRatio</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-screenpixelratio"/>
    [IdlMember("screenPixelRatio", "https://w3c.github.io/mediacapture-screen-share/#dom-mediatracksettings-screenpixelratio")]
    public double? ScreenPixelRatio { get; init; }

    /// <summary>Gets the current value of the <c>whiteBalanceMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-whitebalancemode"/>
    [IdlMember("whiteBalanceMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-whitebalancemode")]
    public string? WhiteBalanceMode { get; init; }

    /// <summary>Gets the current value of the <c>exposureMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposuremode"/>
    [IdlMember("exposureMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposuremode")]
    public string? ExposureMode { get; init; }

    /// <summary>Gets the current value of the <c>focusMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-focusmode"/>
    [IdlMember("focusMode", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-focusmode")]
    public string? FocusMode { get; init; }

    /// <summary>Gets the current value of the <c>pointsOfInterest</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-pointsofinterest"/>
    [IdlMember("pointsOfInterest", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-pointsofinterest")]
    public IReadOnlyList<Point2D>? PointsOfInterest { get; init; }

    /// <summary>Gets the current value of the <c>exposureCompensation</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposurecompensation"/>
    [IdlMember("exposureCompensation", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposurecompensation")]
    public double? ExposureCompensation { get; init; }

    /// <summary>Gets the current value of the <c>exposureTime</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposuretime"/>
    [IdlMember("exposureTime", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-exposuretime")]
    public double? ExposureTime { get; init; }

    /// <summary>Gets the current value of the <c>colorTemperature</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-colortemperature"/>
    [IdlMember("colorTemperature", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-colortemperature")]
    public double? ColorTemperature { get; init; }

    /// <summary>Gets the current value of the <c>iso</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-iso"/>
    [IdlMember("iso", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-iso")]
    public double? Iso { get; init; }

    /// <summary>Gets the current value of the <c>brightness</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-brightness"/>
    [IdlMember("brightness", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-brightness")]
    public double? Brightness { get; init; }

    /// <summary>Gets the current value of the <c>contrast</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-contrast"/>
    [IdlMember("contrast", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-contrast")]
    public double? Contrast { get; init; }

    /// <summary>Gets the current value of the <c>saturation</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-saturation"/>
    [IdlMember("saturation", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-saturation")]
    public double? Saturation { get; init; }

    /// <summary>Gets the current value of the <c>sharpness</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-sharpness"/>
    [IdlMember("sharpness", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-sharpness")]
    public double? Sharpness { get; init; }

    /// <summary>Gets the current value of the <c>focusDistance</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-focusdistance"/>
    [IdlMember("focusDistance", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-focusdistance")]
    public double? FocusDistance { get; init; }

    /// <summary>Gets the current value of the <c>pan</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-pan"/>
    [IdlMember("pan", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-pan")]
    public double? Pan { get; init; }

    /// <summary>Gets the current value of the <c>tilt</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-tilt"/>
    [IdlMember("tilt", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-tilt")]
    public double? Tilt { get; init; }

    /// <summary>Gets the current value of the <c>zoom</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-zoom"/>
    [IdlMember("zoom", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-zoom")]
    public double? Zoom { get; init; }

    /// <summary>Gets the current value of the <c>torch</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-torch"/>
    [IdlMember("torch", "https://w3c.github.io/mediacapture-image/#dom-mediatracksettings-torch")]
    public bool? Torch { get; init; }

    /// <summary>Gets the current value of the <c>powerEfficient</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-powerefficient"/>
    [IdlMember("powerEfficient", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-powerefficient")]
    public bool? PowerEfficient { get; init; }

    /// <summary>Gets the current value of the <c>powerEfficientPixelFormat</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-powerefficientpixelformat"/>
    [IdlMember("powerEfficientPixelFormat", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-powerefficientpixelformat")]
    public bool? PowerEfficientPixelFormat { get; init; }

    /// <summary>Gets the current value of the <c>gestureReactions</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-gesturereactions"/>
    [IdlMember("gestureReactions", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-gesturereactions")]
    public bool? GestureReactions { get; init; }

    /// <summary>Gets the current value of the <c>faceFraming</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-faceframing"/>
    [IdlMember("faceFraming", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-faceframing")]
    public bool? FaceFraming { get; init; }

    /// <summary>Gets the current value of the <c>eyeGazeCorrection</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-eyegazecorrection"/>
    [IdlMember("eyeGazeCorrection", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-eyegazecorrection")]
    public bool? EyeGazeCorrection { get; init; }

    /// <summary>Gets the current value of the <c>voiceIsolation</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-voiceisolation"/>
    [IdlMember("voiceIsolation", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-voiceisolation")]
    public bool? VoiceIsolation { get; init; }

    /// <summary>Gets the current value of the <c>humanFaceDetectionMode</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-humanfacedetectionmode"/>
    [IdlMember("humanFaceDetectionMode", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-humanfacedetectionmode")]
    public string? HumanFaceDetectionMode { get; init; }

    /// <summary>Gets the current value of the <c>backgroundSegmentationMask</c> constrainable property, or null when the source does not have the property.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-backgroundsegmentationmask"/>
    [IdlMember("backgroundSegmentationMask", "https://w3c.github.io/mediacapture-extensions/#dom-mediatracksettings-backgroundsegmentationmask")]
    public bool? BackgroundSegmentationMask { get; init; }
}

/// <summary>
/// A constrainable property (https://w3c.github.io/mediacapture-main/#constrainable-properties): its name, the kinds of
/// tracks it applies to, the type of its values, and the members of the dictionaries that hold its constraints, its
/// capabilities and its setting.
/// </summary>
internal sealed partial class MediaTrackConstrainableProperty
{
    /// <summary>The constrainable properties that the library supports, in the order of the IDL.</summary>
    public static IReadOnlyList<MediaTrackConstrainableProperty> All { get; } =
    [
        new("width", MediaTrackKinds.Video, MediaTrackValueType.Integer, static set => set.Width, static capabilities => capabilities.Width, static settings => settings.Width),
        new("height", MediaTrackKinds.Video, MediaTrackValueType.Integer, static set => set.Height, static capabilities => capabilities.Height, static settings => settings.Height),
        new("aspectRatio", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.AspectRatio, static capabilities => capabilities.AspectRatio, static settings => settings.AspectRatio),
        new("frameRate", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.FrameRate, static capabilities => capabilities.FrameRate, static settings => settings.FrameRate),
        new("facingMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.FacingMode, static capabilities => capabilities.FacingMode, static settings => settings.FacingMode),
        new("resizeMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.ResizeMode, static capabilities => capabilities.ResizeMode, static settings => settings.ResizeMode),
        new("sampleRate", MediaTrackKinds.Audio, MediaTrackValueType.Integer, static set => set.SampleRate, static capabilities => capabilities.SampleRate, static settings => settings.SampleRate),
        new("sampleSize", MediaTrackKinds.Audio, MediaTrackValueType.Integer, static set => set.SampleSize, static capabilities => capabilities.SampleSize, static settings => settings.SampleSize),
        new("echoCancellation", MediaTrackKinds.Audio, MediaTrackValueType.BooleanOrString, static set => set.EchoCancellation, static capabilities => capabilities.EchoCancellation, static settings => settings.EchoCancellation),
        new("autoGainControl", MediaTrackKinds.Audio, MediaTrackValueType.Boolean, static set => set.AutoGainControl, static capabilities => capabilities.AutoGainControl, static settings => settings.AutoGainControl),
        new("noiseSuppression", MediaTrackKinds.Audio, MediaTrackValueType.Boolean, static set => set.NoiseSuppression, static capabilities => capabilities.NoiseSuppression, static settings => settings.NoiseSuppression),
        new("latency", MediaTrackKinds.Audio, MediaTrackValueType.Number, static set => set.Latency, static capabilities => capabilities.Latency, static settings => settings.Latency),
        new("channelCount", MediaTrackKinds.Audio, MediaTrackValueType.Integer, static set => set.ChannelCount, static capabilities => capabilities.ChannelCount, static settings => settings.ChannelCount),
        new("deviceId", MediaTrackKinds.Audio | MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.DeviceId, static capabilities => capabilities.DeviceId, static settings => settings.DeviceId),
        new("groupId", MediaTrackKinds.Audio | MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.GroupId, static capabilities => capabilities.GroupId, static settings => settings.GroupId),
        new("backgroundBlur", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.BackgroundBlur, static capabilities => capabilities.BackgroundBlur, static settings => settings.BackgroundBlur),
        new("displaySurface", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.DisplaySurface, static capabilities => capabilities.DisplaySurface, static settings => settings.DisplaySurface),
        new("logicalSurface", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.LogicalSurface, static capabilities => capabilities.LogicalSurface, static settings => settings.LogicalSurface),
        new("cursor", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.Cursor, static capabilities => capabilities.Cursor, static settings => settings.Cursor),
        new("restrictOwnAudio", MediaTrackKinds.Audio, MediaTrackValueType.Boolean, static set => set.RestrictOwnAudio, static _ => null, static settings => settings.RestrictOwnAudio),
        new("suppressLocalAudioPlayback", MediaTrackKinds.Audio, MediaTrackValueType.Boolean, static set => set.SuppressLocalAudioPlayback, static _ => null, static settings => settings.SuppressLocalAudioPlayback),
        new("whiteBalanceMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.WhiteBalanceMode, static capabilities => capabilities.WhiteBalanceMode, static settings => settings.WhiteBalanceMode),
        new("exposureMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.ExposureMode, static capabilities => capabilities.ExposureMode, static settings => settings.ExposureMode),
        new("focusMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.FocusMode, static capabilities => capabilities.FocusMode, static settings => settings.FocusMode),
        new("pointsOfInterest", MediaTrackKinds.Video, MediaTrackValueType.Points, static set => set.PointsOfInterest, static _ => null, static settings => settings.PointsOfInterest),
        new("exposureCompensation", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.ExposureCompensation, static capabilities => capabilities.ExposureCompensation, static settings => settings.ExposureCompensation),
        new("exposureTime", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.ExposureTime, static capabilities => capabilities.ExposureTime, static settings => settings.ExposureTime),
        new("colorTemperature", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.ColorTemperature, static capabilities => capabilities.ColorTemperature, static settings => settings.ColorTemperature),
        new("iso", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Iso, static capabilities => capabilities.Iso, static settings => settings.Iso),
        new("brightness", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Brightness, static capabilities => capabilities.Brightness, static settings => settings.Brightness),
        new("contrast", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Contrast, static capabilities => capabilities.Contrast, static settings => settings.Contrast),
        new("pan", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Pan, static capabilities => capabilities.Pan, static settings => settings.Pan),
        new("saturation", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Saturation, static capabilities => capabilities.Saturation, static settings => settings.Saturation),
        new("sharpness", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Sharpness, static capabilities => capabilities.Sharpness, static settings => settings.Sharpness),
        new("focusDistance", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.FocusDistance, static capabilities => capabilities.FocusDistance, static settings => settings.FocusDistance),
        new("tilt", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Tilt, static capabilities => capabilities.Tilt, static settings => settings.Tilt),
        new("zoom", MediaTrackKinds.Video, MediaTrackValueType.Number, static set => set.Zoom, static capabilities => capabilities.Zoom, static settings => settings.Zoom),
        new("torch", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.Torch, static capabilities => capabilities.Torch, static settings => settings.Torch),
        new("powerEfficient", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static _ => null, static capabilities => capabilities.PowerEfficient, static settings => settings.PowerEfficient),
        new("powerEfficientPixelFormat", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static _ => null, static capabilities => capabilities.PowerEfficientPixelFormat, static settings => settings.PowerEfficientPixelFormat),
        new("gestureReactions", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.GestureReactions, static capabilities => capabilities.GestureReactions, static settings => settings.GestureReactions),
        new("faceFraming", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.FaceFraming, static capabilities => capabilities.FaceFraming, static settings => settings.FaceFraming),
        new("eyeGazeCorrection", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.EyeGazeCorrection, static capabilities => capabilities.EyeGazeCorrection, static settings => settings.EyeGazeCorrection),
        new("voiceIsolation", MediaTrackKinds.Audio, MediaTrackValueType.Boolean, static set => set.VoiceIsolation, static capabilities => capabilities.VoiceIsolation, static settings => settings.VoiceIsolation),
        new("humanFaceDetectionMode", MediaTrackKinds.Video, MediaTrackValueType.String, static set => set.HumanFaceDetectionMode, static capabilities => capabilities.HumanFaceDetectionMode, static settings => settings.HumanFaceDetectionMode),
        new("backgroundSegmentationMask", MediaTrackKinds.Video, MediaTrackValueType.Boolean, static set => set.BackgroundSegmentationMask, static capabilities => capabilities.BackgroundSegmentationMask, static settings => settings.BackgroundSegmentationMask),
        new("screenPixelRatio", MediaTrackKinds.Video, MediaTrackValueType.Number, static _ => null, static _ => null, static settings => settings.ScreenPixelRatio),
    ];

    /// <summary>Creates the settings dictionary of the values of the constrainable properties, by name.</summary>
    public static MediaTrackSettings CreateSettings(IReadOnlyDictionary<string, object> values) => new()
    {
        Width = GetInteger(values, "width"),
        Height = GetInteger(values, "height"),
        AspectRatio = GetNumber(values, "aspectRatio"),
        FrameRate = GetNumber(values, "frameRate"),
        FacingMode = GetString(values, "facingMode"),
        ResizeMode = GetString(values, "resizeMode"),
        SampleRate = GetInteger(values, "sampleRate"),
        SampleSize = GetInteger(values, "sampleSize"),
        EchoCancellation = GetBooleanOrString(values, "echoCancellation"),
        AutoGainControl = GetBoolean(values, "autoGainControl"),
        NoiseSuppression = GetBoolean(values, "noiseSuppression"),
        Latency = GetNumber(values, "latency"),
        ChannelCount = GetInteger(values, "channelCount"),
        DeviceId = GetString(values, "deviceId"),
        GroupId = GetString(values, "groupId"),
        BackgroundBlur = GetBoolean(values, "backgroundBlur"),
        DisplaySurface = GetString(values, "displaySurface"),
        LogicalSurface = GetBoolean(values, "logicalSurface"),
        Cursor = GetString(values, "cursor"),
        RestrictOwnAudio = GetBoolean(values, "restrictOwnAudio"),
        SuppressLocalAudioPlayback = GetBoolean(values, "suppressLocalAudioPlayback"),
        WhiteBalanceMode = GetString(values, "whiteBalanceMode"),
        ExposureMode = GetString(values, "exposureMode"),
        FocusMode = GetString(values, "focusMode"),
        PointsOfInterest = GetPoints(values, "pointsOfInterest"),
        ExposureCompensation = GetNumber(values, "exposureCompensation"),
        ExposureTime = GetNumber(values, "exposureTime"),
        ColorTemperature = GetNumber(values, "colorTemperature"),
        Iso = GetNumber(values, "iso"),
        Brightness = GetNumber(values, "brightness"),
        Contrast = GetNumber(values, "contrast"),
        Pan = GetNumber(values, "pan"),
        Saturation = GetNumber(values, "saturation"),
        Sharpness = GetNumber(values, "sharpness"),
        FocusDistance = GetNumber(values, "focusDistance"),
        Tilt = GetNumber(values, "tilt"),
        Zoom = GetNumber(values, "zoom"),
        Torch = GetBoolean(values, "torch"),
        PowerEfficient = GetBoolean(values, "powerEfficient"),
        PowerEfficientPixelFormat = GetBoolean(values, "powerEfficientPixelFormat"),
        GestureReactions = GetBoolean(values, "gestureReactions"),
        FaceFraming = GetBoolean(values, "faceFraming"),
        EyeGazeCorrection = GetBoolean(values, "eyeGazeCorrection"),
        VoiceIsolation = GetBoolean(values, "voiceIsolation"),
        HumanFaceDetectionMode = GetString(values, "humanFaceDetectionMode"),
        BackgroundSegmentationMask = GetBoolean(values, "backgroundSegmentationMask"),
        ScreenPixelRatio = GetNumber(values, "screenPixelRatio"),
    };

    /// <summary>Creates the capabilities dictionary of the capabilities of the constrainable properties (the values of the members), by name.</summary>
    public static MediaTrackCapabilities CreateCapabilities(IReadOnlyDictionary<string, object> values) => new()
    {
        Width = values.GetValueOrDefault("width") as ULongRange,
        Height = values.GetValueOrDefault("height") as ULongRange,
        AspectRatio = values.GetValueOrDefault("aspectRatio") as DoubleRange,
        FrameRate = values.GetValueOrDefault("frameRate") as DoubleRange,
        FacingMode = values.GetValueOrDefault("facingMode") as IReadOnlyList<string>,
        ResizeMode = values.GetValueOrDefault("resizeMode") as IReadOnlyList<string>,
        SampleRate = values.GetValueOrDefault("sampleRate") as ULongRange,
        SampleSize = values.GetValueOrDefault("sampleSize") as ULongRange,
        EchoCancellation = values.GetValueOrDefault("echoCancellation") as IReadOnlyList<BooleanOrString>,
        AutoGainControl = values.GetValueOrDefault("autoGainControl") as IReadOnlyList<bool>,
        NoiseSuppression = values.GetValueOrDefault("noiseSuppression") as IReadOnlyList<bool>,
        Latency = values.GetValueOrDefault("latency") as DoubleRange,
        ChannelCount = values.GetValueOrDefault("channelCount") as ULongRange,
        DeviceId = values.GetValueOrDefault("deviceId") as string,
        GroupId = values.GetValueOrDefault("groupId") as string,
        BackgroundBlur = values.GetValueOrDefault("backgroundBlur") as IReadOnlyList<bool>,
        DisplaySurface = values.GetValueOrDefault("displaySurface") as string,
        LogicalSurface = values.GetValueOrDefault("logicalSurface") as bool?,
        Cursor = values.GetValueOrDefault("cursor") as IReadOnlyList<string>,
        WhiteBalanceMode = values.GetValueOrDefault("whiteBalanceMode") as IReadOnlyList<string>,
        ExposureMode = values.GetValueOrDefault("exposureMode") as IReadOnlyList<string>,
        FocusMode = values.GetValueOrDefault("focusMode") as IReadOnlyList<string>,
        ExposureCompensation = values.GetValueOrDefault("exposureCompensation") as MediaSettingsRange,
        ExposureTime = values.GetValueOrDefault("exposureTime") as MediaSettingsRange,
        ColorTemperature = values.GetValueOrDefault("colorTemperature") as MediaSettingsRange,
        Iso = values.GetValueOrDefault("iso") as MediaSettingsRange,
        Brightness = values.GetValueOrDefault("brightness") as MediaSettingsRange,
        Contrast = values.GetValueOrDefault("contrast") as MediaSettingsRange,
        Pan = values.GetValueOrDefault("pan") as MediaSettingsRange,
        Saturation = values.GetValueOrDefault("saturation") as MediaSettingsRange,
        Sharpness = values.GetValueOrDefault("sharpness") as MediaSettingsRange,
        FocusDistance = values.GetValueOrDefault("focusDistance") as MediaSettingsRange,
        Tilt = values.GetValueOrDefault("tilt") as MediaSettingsRange,
        Zoom = values.GetValueOrDefault("zoom") as MediaSettingsRange,
        Torch = values.GetValueOrDefault("torch") as IReadOnlyList<bool>,
        PowerEfficient = values.GetValueOrDefault("powerEfficient") as IReadOnlyList<bool>,
        PowerEfficientPixelFormat = values.GetValueOrDefault("powerEfficientPixelFormat") as IReadOnlyList<bool>,
        GestureReactions = values.GetValueOrDefault("gestureReactions") as IReadOnlyList<bool>,
        FaceFraming = values.GetValueOrDefault("faceFraming") as IReadOnlyList<bool>,
        EyeGazeCorrection = values.GetValueOrDefault("eyeGazeCorrection") as IReadOnlyList<bool>,
        VoiceIsolation = values.GetValueOrDefault("voiceIsolation") as IReadOnlyList<bool>,
        HumanFaceDetectionMode = values.GetValueOrDefault("humanFaceDetectionMode") as IReadOnlyList<string>,
        BackgroundSegmentationMask = values.GetValueOrDefault("backgroundSegmentationMask") as IReadOnlyList<bool>,
    };
}
