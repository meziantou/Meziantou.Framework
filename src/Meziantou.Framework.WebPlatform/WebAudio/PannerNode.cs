using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The spatialization algorithms of a <see cref="PannerNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-panningmodeltype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-panningmodeltype"/>
[IdlInterface("PanningModelType", "https://webaudio.github.io/web-audio-api/#enumdef-panningmodeltype")]
public enum PanningModelType
{
    /// <summary>An equal-power panning (the elevation is ignored).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panningmodeltype-equalpower"/>
    [IdlMember("equalpower", "https://webaudio.github.io/web-audio-api/#dom-panningmodeltype-equalpower")]
    EqualPower,

    /// <summary>A head-related transfer function (a structural model of the head and of the pinnae).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panningmodeltype-hrtf"/>
    [IdlMember("HRTF", "https://webaudio.github.io/web-audio-api/#dom-panningmodeltype-hrtf")]
    Hrtf,
}

/// <summary>The distance models of a <see cref="PannerNode"/> (https://webaudio.github.io/web-audio-api/#enumdef-distancemodeltype).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#enumdef-distancemodeltype"/>
[IdlInterface("DistanceModelType", "https://webaudio.github.io/web-audio-api/#enumdef-distancemodeltype")]
public enum DistanceModelType
{
    /// <summary>A linear attenuation between the reference and the maximum distances.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-linear"/>
    [IdlMember("linear", "https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-linear")]
    Linear,

    /// <summary>An attenuation inversely proportional to the distance.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-inverse"/>
    [IdlMember("inverse", "https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-inverse")]
    Inverse,

    /// <summary>An exponential attenuation.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-exponential"/>
    [IdlMember("exponential", "https://webaudio.github.io/web-audio-api/#dom-distancemodeltype-exponential")]
    Exponential,
}

/// <summary>The options of a <see cref="PannerNode"/> (https://webaudio.github.io/web-audio-api/#PannerOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#PannerOptions"/>
[IdlInterface("PannerOptions", "https://webaudio.github.io/web-audio-api/#PannerOptions")]
public sealed class PannerOptions : AudioNodeOptions
{
    /// <summary>Gets the spatialization algorithm.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-panningmodel"/>
    [IdlMember("panningModel", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-panningmodel")]
    public PanningModelType PanningModel { get; init; }

    /// <summary>Gets the distance model.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-distancemodel"/>
    [IdlMember("distanceModel", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-distancemodel")]
    public DistanceModelType DistanceModel { get; init; } = DistanceModelType.Inverse;

    /// <summary>Gets the x coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-positionx"/>
    [IdlMember("positionX", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-positionx")]
    public float PositionX { get; init; }

    /// <summary>Gets the y coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-positiony"/>
    [IdlMember("positionY", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-positiony")]
    public float PositionY { get; init; }

    /// <summary>Gets the z coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-positionz"/>
    [IdlMember("positionZ", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-positionz")]
    public float PositionZ { get; init; }

    /// <summary>Gets the x coordinate of the orientation.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationx"/>
    [IdlMember("orientationX", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationx")]
    public float OrientationX { get; init; } = 1;

    /// <summary>Gets the y coordinate of the orientation.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationy"/>
    [IdlMember("orientationY", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationy")]
    public float OrientationY { get; init; }

    /// <summary>Gets the z coordinate of the orientation.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationz"/>
    [IdlMember("orientationZ", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-orientationz")]
    public float OrientationZ { get; init; }

    /// <summary>Gets the reference distance.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-refdistance"/>
    [IdlMember("refDistance", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-refdistance")]
    public double RefDistance { get; init; } = 1;

    /// <summary>Gets the maximum distance.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-maxdistance"/>
    [IdlMember("maxDistance", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-maxdistance")]
    public double MaxDistance { get; init; } = 10000;

    /// <summary>Gets the rolloff factor.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-rollofffactor"/>
    [IdlMember("rolloffFactor", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-rollofffactor")]
    public double RolloffFactor { get; init; } = 1;

    /// <summary>Gets the angle in degrees of the inner cone.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneinnerangle"/>
    [IdlMember("coneInnerAngle", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneinnerangle")]
    public double ConeInnerAngle { get; init; } = 360;

    /// <summary>Gets the angle in degrees of the outer cone.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneouterangle"/>
    [IdlMember("coneOuterAngle", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneouterangle")]
    public double ConeOuterAngle { get; init; } = 360;

    /// <summary>Gets the gain outside of the outer cone.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneoutergain"/>
    [IdlMember("coneOuterGain", "https://webaudio.github.io/web-audio-api/#dom-panneroptions-coneoutergain")]
    public double ConeOuterGain { get; init; }
}

/// <summary>
/// A node that spatializes its input relative to the <see cref="BaseAudioContext.Listener"/>
/// (https://webaudio.github.io/web-audio-api/#PannerNode): its output is stereo, panned by the azimuth (equal power, or a
/// structural head-related transfer function), and attenuated by the distance and the sound cone.
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#PannerNode"/>
[IdlInterface("PannerNode", "https://webaudio.github.io/web-audio-api/#PannerNode")]
public sealed class PannerNode : AudioNode
{
    private double _refDistance = 1;
    private double _maxDistance = 10000;
    private double _rolloffFactor = 1;
    private double _coneOuterGain;
    private HrtfModel? _hrtf;

    /// <summary>Creates a panner.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-pannernode"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-pannernode-pannernode")]
    public PannerNode(BaseAudioContext context, PannerOptions? options = null)
        : base(context, numberOfInputs: 1, numberOfOutputs: 1, channelCount: 2, ChannelCountMode.ClampedMax, ChannelInterpretation.Speakers)
    {
        PositionX = CreateParameter(context, "positionX", 0);
        PositionY = CreateParameter(context, "positionY", 0);
        PositionZ = CreateParameter(context, "positionZ", 0);
        OrientationX = CreateParameter(context, "orientationX", 1);
        OrientationY = CreateParameter(context, "orientationY", 0);
        OrientationZ = CreateParameter(context, "orientationZ", 0);
        ApplyOptions(options);
        if (options is not null)
        {
            PanningModel = options.PanningModel;
            DistanceModel = options.DistanceModel;
            PositionX.SetInitialValue(options.PositionX);
            PositionY.SetInitialValue(options.PositionY);
            PositionZ.SetInitialValue(options.PositionZ);
            OrientationX.SetInitialValue(options.OrientationX);
            OrientationY.SetInitialValue(options.OrientationY);
            OrientationZ.SetInitialValue(options.OrientationZ);
            RefDistance = options.RefDistance;
            MaxDistance = options.MaxDistance;
            RolloffFactor = options.RolloffFactor;
            ConeInnerAngle = options.ConeInnerAngle;
            ConeOuterAngle = options.ConeOuterAngle;
            ConeOuterGain = options.ConeOuterGain;
        }
    }

    private AudioParam CreateParameter(BaseAudioContext context, string name, float defaultValue) => new(context, this, "PannerNode." + name, defaultValue, -float.MaxValue, float.MaxValue, AutomationRate.ARate);

    /// <summary>Gets or sets the spatialization algorithm.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-panningmodel"/>
    [IdlMember("panningModel", "https://webaudio.github.io/web-audio-api/#dom-pannernode-panningmodel")]
    public PanningModelType PanningModel { get; set; }

    /// <summary>Gets the x coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-positionx"/>
    [IdlMember("positionX", "https://webaudio.github.io/web-audio-api/#dom-pannernode-positionx")]
    public AudioParam PositionX { get; }

    /// <summary>Gets the y coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-positiony"/>
    [IdlMember("positionY", "https://webaudio.github.io/web-audio-api/#dom-pannernode-positiony")]
    public AudioParam PositionY { get; }

    /// <summary>Gets the z coordinate of the position.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-positionz"/>
    [IdlMember("positionZ", "https://webaudio.github.io/web-audio-api/#dom-pannernode-positionz")]
    public AudioParam PositionZ { get; }

    /// <summary>Gets the x coordinate of the direction the source points to.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationx"/>
    [IdlMember("orientationX", "https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationx")]
    public AudioParam OrientationX { get; }

    /// <summary>Gets the y coordinate of the direction the source points to.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationy"/>
    [IdlMember("orientationY", "https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationy")]
    public AudioParam OrientationY { get; }

    /// <summary>Gets the z coordinate of the direction the source points to.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationz"/>
    [IdlMember("orientationZ", "https://webaudio.github.io/web-audio-api/#dom-pannernode-orientationz")]
    public AudioParam OrientationZ { get; }

    /// <summary>Gets or sets the distance model.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-distancemodel"/>
    [IdlMember("distanceModel", "https://webaudio.github.io/web-audio-api/#dom-pannernode-distancemodel")]
    public DistanceModelType DistanceModel { get; set; } = DistanceModelType.Inverse;

    /// <summary>Gets or sets the reference distance (the volume is not reduced for smaller distances).</summary>
    /// <exception cref="RangeErrorException">The value is negative.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-refdistance"/>
    [IdlMember("refDistance", "https://webaudio.github.io/web-audio-api/#dom-pannernode-refdistance")]
    public double RefDistance
    {
        get => _refDistance;
        set
        {
            if (value < 0)
                throw new RangeErrorException($"The reference distance {value} cannot be negative");

            _refDistance = value;
        }
    }

    /// <summary>Gets or sets the maximum distance (the volume is not reduced further for greater distances).</summary>
    /// <exception cref="RangeErrorException">The value is not positive.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-maxdistance"/>
    [IdlMember("maxDistance", "https://webaudio.github.io/web-audio-api/#dom-pannernode-maxdistance")]
    public double MaxDistance
    {
        get => _maxDistance;
        set
        {
            if (value <= 0)
                throw new RangeErrorException($"The maximum distance {value} must be positive");

            _maxDistance = value;
        }
    }

    /// <summary>Gets or sets how quickly the volume is reduced with the distance.</summary>
    /// <exception cref="RangeErrorException">The value is negative.</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-rollofffactor"/>
    [IdlMember("rolloffFactor", "https://webaudio.github.io/web-audio-api/#dom-pannernode-rollofffactor")]
    public double RolloffFactor
    {
        get => _rolloffFactor;
        set
        {
            if (value < 0)
                throw new RangeErrorException($"The rolloff factor {value} cannot be negative");

            _rolloffFactor = value;
        }
    }

    /// <summary>Gets or sets the angle in degrees of the inner cone, in which the volume is not reduced.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-coneinnerangle"/>
    [IdlMember("coneInnerAngle", "https://webaudio.github.io/web-audio-api/#dom-pannernode-coneinnerangle")]
    public double ConeInnerAngle { get; set; } = 360;

    /// <summary>Gets or sets the angle in degrees of the outer cone, outside of which the volume is <see cref="ConeOuterGain"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-coneouterangle"/>
    [IdlMember("coneOuterAngle", "https://webaudio.github.io/web-audio-api/#dom-pannernode-coneouterangle")]
    public double ConeOuterAngle { get; set; } = 360;

    /// <summary>Gets or sets the gain outside of the outer cone, from 0 to 1.</summary>
    /// <exception cref="DomException">The value is not between 0 and 1 (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-coneoutergain"/>
    [IdlMember("coneOuterGain", "https://webaudio.github.io/web-audio-api/#dom-pannernode-coneoutergain")]
    public double ConeOuterGain
    {
        get => _coneOuterGain;
        set
        {
            if (!(value >= 0 && value <= 1))
                throw new DomException($"The cone outer gain {value} is not between 0 and 1", "InvalidStateError");

            _coneOuterGain = value;
        }
    }

    /// <summary>Sets the position (the values of the position parameters).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-setposition"/>
    [IdlMember("setPosition", "https://webaudio.github.io/web-audio-api/#dom-pannernode-setposition")]
    public void SetPosition(float x, float y, float z)
    {
        PositionX.Value = x;
        PositionY.Value = y;
        PositionZ.Value = z;
    }

    /// <summary>Sets the direction the source points to (the values of the orientation parameters).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-pannernode-setorientation"/>
    [IdlMember("setOrientation", "https://webaudio.github.io/web-audio-api/#dom-pannernode-setorientation")]
    public void SetOrientation(float x, float y, float z)
    {
        OrientationX.Value = x;
        OrientationY.Value = y;
        OrientationZ.Value = z;
    }

    private protected override IEnumerable<AudioParam> NodeParameters => [PositionX, PositionY, PositionZ, OrientationX, OrientationY, OrientationZ, .. Context.Listener.Parameters];

    // The parameters of an HRTF panner are k-rate (https://webaudio.github.io/web-audio-api/#dom-audioparam-automationrate)
    internal override bool ForcesKRate(AudioParam parameter) => PanningModel == PanningModelType.Hrtf;

    private protected override long TailFrames => PanningModel == PanningModelType.Hrtf ? HrtfModel.GetLatencyFrames(Context.SampleRate) : 0;

    private protected override void ValidateChannelCount(uint value)
    {
        if (value > 2)
            throw new DomException($"The channel count of a panner cannot be greater than 2 ({value})", "NotSupportedError");

        base.ValidateChannelCount(value);
    }

    private protected override void ValidateChannelCountMode(ChannelCountMode value)
    {
        if (value == ChannelCountMode.Max)
            throw new DomException("The channel count mode of a panner cannot be 'max'", "NotSupportedError");
    }

    private protected override void OnBecameInactive() => _hrtf?.Reset();

    private protected override void Process(long startFrame, int length)
    {
        var input = GetInputBus(0);
        var output = GetOutputBus(0);
        output.SetNumberOfChannels(2);
        var left = output.GetChannelSpan(0);
        var right = output.GetChannelSpan(1);
        var listener = Context.Listener;
        AudioParam[] parameters = [PositionX, PositionY, PositionZ, OrientationX, OrientationY, OrientationZ, listener.PositionX, listener.PositionY, listener.PositionZ, listener.ForwardX, listener.ForwardY, listener.ForwardZ, listener.UpX, listener.UpY, listener.UpZ];
        var constant = parameters.All(p => p.IsConstant);
        if (PanningModel == PanningModelType.Hrtf)
        {
            var geometry = ComputeGeometry(parameters, 0, constant: true);
            _hrtf ??= new HrtfModel(Context.SampleRate);
            _hrtf.Process(input, geometry.Azimuth, geometry.Elevation, geometry.Gain, left, right);
            output.MarkNonSilent();
            return;
        }

        if (input.IsSilent)
        {
            output.Zero(2);
            return;
        }

        var mono = input.NumberOfChannels == 1;
        var inputLeft = input.GetChannelSpan(0);
        var inputRight = mono ? inputLeft : input.GetChannelSpan(1);
        var constantGeometry = constant ? ComputeGeometry(parameters, 0, constant: true) : default;
        for (var i = 0; i < length; i++)
        {
            var geometry = constant ? constantGeometry : ComputeGeometry(parameters, i, constant: false);

            // https://webaudio.github.io/web-audio-api/#Spatialization-equal-power-panning
            var azimuth = Math.Clamp(geometry.Azimuth, -180, 180);
            if (azimuth < -90)
            {
                azimuth = -180 - azimuth;
            }
            else if (azimuth > 90)
            {
                azimuth = 180 - azimuth;
            }

            double x;
            if (mono)
            {
                x = (azimuth + 90) / 180;
            }
            else
            {
                x = azimuth <= 0 ? (azimuth + 90) / 90 : azimuth / 90;
            }

            var gainLeft = Math.Cos(x * Math.PI / 2);
            var gainRight = Math.Sin(x * Math.PI / 2);
            double outLeft;
            double outRight;
            if (mono)
            {
                outLeft = inputLeft[i] * gainLeft;
                outRight = inputLeft[i] * gainRight;
            }
            else if (azimuth <= 0)
            {
                outLeft = inputLeft[i] + (inputRight[i] * gainLeft);
                outRight = inputRight[i] * gainRight;
            }
            else
            {
                outLeft = inputLeft[i] * gainLeft;
                outRight = inputRight[i] + (inputLeft[i] * gainRight);
            }

            // As in the browsers, the panned samples and the gain (distance and cone) are single-precision floats
            var totalGain = (double)(float)geometry.Gain;
            left[i] = (float)((float)outLeft * totalGain);
            right[i] = (float)((float)outRight * totalGain);
        }

        output.MarkNonSilent();
    }

    /// <summary>The azimuth, the elevation and the gain (distance and cone) of a frame.</summary>
    private Geometry ComputeGeometry(AudioParam[] parameters, int frame, bool constant)
    {
        double Value(int index)
        {
            var parameter = parameters[index];
            return constant || parameter.IsConstant ? parameter.FinalValue : parameter.Values[frame];
        }

        var sourcePosition = new Vector3D(Value(0), Value(1), Value(2));
        var sourceOrientation = new Vector3D(Value(3), Value(4), Value(5));
        var listenerPosition = new Vector3D(Value(6), Value(7), Value(8));
        var listenerForward = new Vector3D(Value(9), Value(10), Value(11));
        var listenerUp = new Vector3D(Value(12), Value(13), Value(14));
        var (azimuth, elevation) = ComputeAzimuthAndElevation(sourcePosition, listenerPosition, listenerForward, listenerUp);
        var distance = (sourcePosition - listenerPosition).Magnitude;
        var gain = ComputeDistanceGain(distance) * ComputeConeGain(sourcePosition, sourceOrientation, listenerPosition);
        return new Geometry(azimuth, elevation, gain);
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#azimuth-elevation</summary>
    private static (double Azimuth, double Elevation) ComputeAzimuthAndElevation(Vector3D sourcePosition, Vector3D listenerPosition, Vector3D listenerForward, Vector3D listenerUp)
    {
        var sourceListener = (sourcePosition - listenerPosition).Normalize();
        if (sourceListener.Magnitude == 0)
            return (0, 0);

        var listenerRight = listenerForward.Cross(listenerUp);
        if (listenerRight.Magnitude == 0)
            return (0, 0);

        var listenerRightNorm = listenerRight.Normalize();
        var listenerForwardNorm = listenerForward.Normalize();
        var up = listenerRightNorm.Cross(listenerForwardNorm);
        var upProjection = sourceListener.Dot(up);
        var projectedSource = (sourceListener - (up * upProjection)).Normalize();
        var azimuth = 180 * Math.Acos(Math.Clamp(projectedSource.Dot(listenerRightNorm), -1, 1)) / Math.PI;
        var frontBack = projectedSource.Dot(listenerForwardNorm);
        if (frontBack < 0)
        {
            azimuth = 360 - azimuth;
        }

        azimuth = azimuth is >= 0 and <= 270 ? 90 - azimuth : 450 - azimuth;
        var elevation = 90 - (180 * Math.Acos(Math.Clamp(sourceListener.Dot(up), -1, 1)) / Math.PI);
        if (elevation > 90)
        {
            elevation = 180 - elevation;
        }
        else if (elevation < -90)
        {
            elevation = -180 - elevation;
        }

        return (azimuth, elevation);
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#enumdef-distancemodeltype</summary>
    private double ComputeDistanceGain(double distance)
    {
        var refDistance = _refDistance;
        var maxDistance = _maxDistance;
        var rolloff = _rolloffFactor;
        switch (DistanceModel)
        {
            case DistanceModelType.Linear:
            {
                var minDistance = Math.Min(refDistance, maxDistance);
                var maxD = Math.Max(refDistance, maxDistance);
                rolloff = Math.Clamp(rolloff, 0, 1);
                if (minDistance == maxD)
                    return 1 - rolloff;

                var d = Math.Clamp(distance, minDistance, maxD);
                return 1 - (rolloff * (d - minDistance) / (maxD - minDistance));
            }

            case DistanceModelType.Inverse:
            {
                if (refDistance == 0)
                    return 0;

                var d = Math.Max(distance, refDistance);
                return refDistance / (refDistance + (rolloff * (d - refDistance)));
            }

            default:
            {
                if (refDistance == 0)
                    return 0;

                var d = Math.Max(distance, refDistance);
                return Math.Pow(d / refDistance, -rolloff);
            }
        }
    }

    /// <summary>https://webaudio.github.io/web-audio-api/#Spatialization-sound-cones</summary>
    private double ComputeConeGain(Vector3D sourcePosition, Vector3D sourceOrientation, Vector3D listenerPosition)
    {
        if (sourceOrientation.Magnitude == 0 || (ConeInnerAngle == 360 && ConeOuterAngle == 360))
            return 1;

        var sourceToListener = (listenerPosition - sourcePosition).Normalize();
        var normalizedOrientation = sourceOrientation.Normalize();
        var angle = 180 * Math.Acos(Math.Clamp(sourceToListener.Dot(normalizedOrientation), -1, 1)) / Math.PI;
        var absAngle = Math.Abs(angle);
        var absInnerAngle = Math.Abs(ConeInnerAngle) / 2;
        var absOuterAngle = Math.Abs(ConeOuterAngle) / 2;
        if (absAngle <= absInnerAngle)
            return 1;

        if (absAngle >= absOuterAngle)
            return _coneOuterGain;

        var x = (absAngle - absInnerAngle) / (absOuterAngle - absInnerAngle);
        return (1 - x) + (_coneOuterGain * x);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Geometry(double Azimuth, double Elevation, double Gain);

    /// <summary>A vector of the spatialization (https://webaudio.github.io/web-audio-api/#Spatialization).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Vector3D(double X, double Y, double Z)
    {
        public double Magnitude => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector3D Normalize()
        {
            var magnitude = Magnitude;
            return magnitude == 0 || !double.IsFinite(magnitude) ? (double.IsInfinity(magnitude) ? NormalizeInfinite() : default) : new(X / magnitude, Y / magnitude, Z / magnitude);
        }

        // A vector with infinite components: the direction of its infinite components
        private Vector3D NormalizeInfinite()
        {
            var x = double.IsInfinity(X) ? Math.Sign(X) : 0;
            var y = double.IsInfinity(Y) ? Math.Sign(Y) : 0;
            var z = double.IsInfinity(Z) ? Math.Sign(Z) : 0;
            return new Vector3D(x, y, z).Normalize();
        }

        public double Dot(Vector3D other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

        public Vector3D Cross(Vector3D other) => new((Y * other.Z) - (Z * other.Y), (Z * other.X) - (X * other.Z), (X * other.Y) - (Y * other.X));

        public static Vector3D operator -(Vector3D left, Vector3D right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3D operator *(Vector3D vector, double scale) => new(vector.X * scale, vector.Y * scale, vector.Z * scale);
    }

    /// <summary>
    /// A structural model of the head-related transfer functions (C. P. Brown and R. O. Duda, "A structural model for binaural
    /// sound synthesis", 1998): an interaural time difference, a head shadow filter per ear, and pinna echoes that depend on
    /// the elevation. There is no measured HRTF database.
    /// </summary>
    private sealed class HrtfModel
    {
        // The radius of the head in meters and the speed of sound in m/s
        private const double HeadRadius = 0.0875;
        private const double SpeedOfSound = 343;

        // The pinna echoes: reflection coefficients and delays in frames at 44.1 kHz
        private static readonly double[] PinnaReflections = [0.5, -1, 0.5, -0.25, 0.25];
        private static readonly double[] PinnaA = [1, 5, 5, 5, 5];
        private static readonly double[] PinnaB = [2, 4, 7, 11, 13];
        private static readonly double[] PinnaD = [1, 0.5, 0.5, 0.5, 0.5];

        private readonly double _sampleRate;
        private readonly float[] _history;
        private int _position;
        private readonly double[] _shadowState = new double[4];

        public HrtfModel(double sampleRate)
        {
            _sampleRate = sampleRate;
            _history = new float[GetLatencyFrames(sampleRate) * 2];
        }

        /// <summary>The longest delay of the model (the interaural time difference and the pinna echoes) in frames.</summary>
        public static int GetLatencyFrames(double sampleRate) => (int)Math.Ceiling(((HeadRadius / SpeedOfSound * (1 + (Math.PI / 2))) + (20 / 44100.0)) * sampleRate) + 2;

        public void Reset()
        {
            Array.Clear(_history);
            Array.Clear(_shadowState);
        }

        public void Process(Rendering.AudioBus input, double azimuth, double elevation, double gain, Span<float> left, Span<float> right)
        {
            // The angle of the source from each ear, in radians (the azimuth is clockwise from the front)
            var theta = azimuth * Math.PI / 180;
            var phi = elevation * Math.PI / 180;
            var leftAngle = Math.Acos(Math.Clamp(-Math.Sin(theta) * Math.Cos(phi), -1, 1));
            var rightAngle = Math.Acos(Math.Clamp(Math.Sin(theta) * Math.Cos(phi), -1, 1));
            var leftDelay = InterauralDelay(leftAngle) * _sampleRate;
            var rightDelay = InterauralDelay(rightAngle) * _sampleRate;
            var (lb0, lb1, la1) = HeadShadow(leftAngle);
            var (rb0, rb1, ra1) = HeadShadow(rightAngle);

            // The pinna delays (in frames) depend on the elevation and on the azimuth
            Span<double> pinnaDelays = stackalloc double[PinnaReflections.Length];
            for (var n = 0; n < PinnaReflections.Length; n++)
            {
                var delay = (PinnaA[n] * Math.Cos(theta / 2) * Math.Sin(PinnaD[n] * ((Math.PI / 2) - phi))) + PinnaB[n];
                pinnaDelays[n] = Math.Max(delay, 1) * _sampleRate / 44100;
            }

            var size = _history.Length;
            for (var i = 0; i < left.Length; i++)
            {
                var sample = 0f;
                if (!input.IsSilent)
                {
                    for (var c = 0; c < input.NumberOfChannels; c++)
                    {
                        sample += input.GetChannel(c)[i];
                    }

                    sample /= input.NumberOfChannels;
                }

                _history[_position] = sample;
                left[i] = (float)(Ear(leftDelay, pinnaDelays, lb0, lb1, la1, 0) * gain);
                right[i] = (float)(Ear(rightDelay, pinnaDelays, rb0, rb1, ra1, 2) * gain);
                _position = (_position + 1) % size;
            }
        }

        private double Ear(double delay, ReadOnlySpan<double> pinnaDelays, double b0, double b1, double a1, int state)
        {
            // The pinna: the direct sound and the echoes, delayed by the interaural time difference
            var x = Read(delay);
            for (var n = 0; n < pinnaDelays.Length; n++)
            {
                x += PinnaReflections[n] * 0.25 * Read(delay + pinnaDelays[n]);
            }

            // The head shadow: a one-pole, one-zero filter
            var y = (b0 * x) + (b1 * _shadowState[state]) - (a1 * _shadowState[state + 1]);
            _shadowState[state] = x;
            _shadowState[state + 1] = y;
            return y;
        }

        private double Read(double delay)
        {
            var size = _history.Length;
            var position = _position - delay;
            var index = Math.Floor(position);
            var fraction = position - index;
            var k = (int)(((long)index % size + size) % size);
            var s0 = _history[k];
            var s1 = _history[(k + 1) % size];
            return s0 + ((s1 - s0) * fraction);
        }

        /// <summary>The delay of the sound to an ear, from the angle between the source and the ear axis.</summary>
        private static double InterauralDelay(double angle)
        {
            var a = HeadRadius / SpeedOfSound;
            return angle < Math.PI / 2 ? a * (1 - Math.Cos(angle)) : a * (angle - (Math.PI / 2) + 1);
        }

        /// <summary>The coefficients of the head shadow filter of an ear (the bilinear transform of the spherical head model).</summary>
        private (double B0, double B1, double A1) HeadShadow(double angle)
        {
            const double MinAlpha = 0.1;
            const double MinAngle = 150 * Math.PI / 180;
            var alpha = 1 + (MinAlpha / 2) + ((1 - (MinAlpha / 2)) * Math.Cos(angle / MinAngle * Math.PI));
            var omega0 = SpeedOfSound / HeadRadius;
            var k = 2 * _sampleRate;
            var denominator = (2 * omega0) + k;
            return (((2 * omega0) + (alpha * k)) / denominator, ((2 * omega0) - (alpha * k)) / denominator, ((2 * omega0) - k) / denominator);
        }
    }
}
