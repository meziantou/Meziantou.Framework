namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The position and the orientation of the listener of the spatialization of the <see cref="PannerNode"/> objects of a
/// context (https://webaudio.github.io/web-audio-api/#AudioListener).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#AudioListener"/>
[IdlInterface("AudioListener", "https://webaudio.github.io/web-audio-api/#AudioListener")]
public sealed class AudioListener
{
    internal AudioListener(BaseAudioContext context)
    {
        PositionX = CreateParameter(context, "positionX", 0);
        PositionY = CreateParameter(context, "positionY", 0);
        PositionZ = CreateParameter(context, "positionZ", 0);
        ForwardX = CreateParameter(context, "forwardX", 0);
        ForwardY = CreateParameter(context, "forwardY", 0);
        ForwardZ = CreateParameter(context, "forwardZ", -1);
        UpX = CreateParameter(context, "upX", 0);
        UpY = CreateParameter(context, "upY", 1);
        UpZ = CreateParameter(context, "upZ", 0);
    }

    private static AudioParam CreateParameter(BaseAudioContext context, string name, float defaultValue) => new(context, owner: null, "AudioListener." + name, defaultValue, -float.MaxValue, float.MaxValue, AutomationRate.ARate);

    /// <summary>Gets the x coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-positionx"/>
    [IdlMember("positionX", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-positionx")]
    public AudioParam PositionX { get; }

    /// <summary>Gets the y coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-positiony"/>
    [IdlMember("positionY", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-positiony")]
    public AudioParam PositionY { get; }

    /// <summary>Gets the z coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-positionz"/>
    [IdlMember("positionZ", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-positionz")]
    public AudioParam PositionZ { get; }

    /// <summary>Gets the x coordinate of the forward direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardx"/>
    [IdlMember("forwardX", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardx")]
    public AudioParam ForwardX { get; }

    /// <summary>Gets the y coordinate of the forward direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardy"/>
    [IdlMember("forwardY", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardy")]
    public AudioParam ForwardY { get; }

    /// <summary>Gets the z coordinate of the forward direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardz"/>
    [IdlMember("forwardZ", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-forwardz")]
    public AudioParam ForwardZ { get; }

    /// <summary>Gets the x coordinate of the up direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-upx"/>
    [IdlMember("upX", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-upx")]
    public AudioParam UpX { get; }

    /// <summary>Gets the y coordinate of the up direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-upy"/>
    [IdlMember("upY", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-upy")]
    public AudioParam UpY { get; }

    /// <summary>Gets the z coordinate of the up direction.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-upz"/>
    [IdlMember("upZ", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-upz")]
    public AudioParam UpZ { get; }

    /// <summary>Sets the position (the values of <see cref="PositionX"/>, <see cref="PositionY"/> and <see cref="PositionZ"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-setposition"/>
    [IdlMember("setPosition", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-setposition")]
    public void SetPosition(float x, float y, float z)
    {
        PositionX.Value = x;
        PositionY.Value = y;
        PositionZ.Value = z;
    }

    /// <summary>Sets the forward and up directions.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiolistener-setorientation"/>
    [IdlMember("setOrientation", "https://webaudio.github.io/web-audio-api/#dom-audiolistener-setorientation")]
    public void SetOrientation(float x, float y, float z, float xUp, float yUp, float zUp)
    {
        ForwardX.Value = x;
        ForwardY.Value = y;
        ForwardZ.Value = z;
        UpX.Value = xUp;
        UpY.Value = yUp;
        UpZ.Value = zUp;
    }

    /// <summary>The parameters of the listener, computed by the panners.</summary>
    internal IEnumerable<AudioParam> Parameters => [PositionX, PositionY, PositionZ, ForwardX, ForwardY, ForwardZ, UpX, UpY, UpZ];
}
