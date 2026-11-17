namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The identifying tags of a DNG file: its version, and the camera and profile that describe its raw data. This is
/// informational only, like <see cref="ImageMetadata.SourceFormat"/>: it describes the file, not the pixels.
/// </summary>
/// <remarks>No operation updates it, and every encoder ignores it.</remarks>
public sealed class DngProperties : IEquatable<DngProperties>
{
    /// <summary>Initializes a new instance of the <see cref="DngProperties"/> class.</summary>
    /// <param name="version">The version of the DNG specification the file follows (<c>DNGVersion</c>).</param>
    /// <param name="backwardVersion">The oldest version of a reader that can read the file (<c>DNGBackwardVersion</c>).</param>
    /// <param name="uniqueCameraModel">The unique, non-localized name of the camera model (<c>UniqueCameraModel</c>), or <see langword="null"/>.</param>
    /// <param name="localizedCameraModel">The localized name of the camera model (<c>LocalizedCameraModel</c>), or <see langword="null"/>.</param>
    /// <param name="make">The manufacturer of the camera (<c>Make</c>), or <see langword="null"/>.</param>
    /// <param name="model">The model of the camera (<c>Model</c>), or <see langword="null"/>.</param>
    /// <param name="cameraSerialNumber">The serial number of the camera (<c>CameraSerialNumber</c>), or <see langword="null"/>.</param>
    /// <param name="profileName">The name of the camera profile of the file (<c>ProfileName</c>), or <see langword="null"/>.</param>
    /// <param name="baselineExposure">The exposure, in stops, that the rendering adds to give the camera model the same brightness as other models (<c>BaselineExposure</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> or <paramref name="backwardVersion"/> is <see langword="null"/>.</exception>
    public DngProperties(Version version, Version backwardVersion, string? uniqueCameraModel, string? localizedCameraModel, string? make, string? model, string? cameraSerialNumber, string? profileName, double baselineExposure)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(backwardVersion);
        Version = version;
        BackwardVersion = backwardVersion;
        UniqueCameraModel = uniqueCameraModel;
        LocalizedCameraModel = localizedCameraModel;
        Make = make;
        Model = model;
        CameraSerialNumber = cameraSerialNumber;
        ProfileName = profileName;
        BaselineExposure = baselineExposure;
    }

    /// <summary>Gets the version of the DNG specification the file follows (<c>DNGVersion</c>), such as 1.4.0.0.</summary>
    public Version Version { get; }

    /// <summary>Gets the oldest version of a reader that can read the file (<c>DNGBackwardVersion</c>).</summary>
    public Version BackwardVersion { get; }

    /// <summary>Gets the unique, non-localized name of the camera model (<c>UniqueCameraModel</c>), or <see langword="null"/>.</summary>
    public string? UniqueCameraModel { get; }

    /// <summary>Gets the localized name of the camera model (<c>LocalizedCameraModel</c>), or <see langword="null"/>.</summary>
    public string? LocalizedCameraModel { get; }

    /// <summary>Gets the manufacturer of the camera (<c>Make</c>), or <see langword="null"/>.</summary>
    public string? Make { get; }

    /// <summary>Gets the model of the camera (<c>Model</c>), or <see langword="null"/>.</summary>
    public string? Model { get; }

    /// <summary>Gets the serial number of the camera (<c>CameraSerialNumber</c>), or <see langword="null"/>.</summary>
    public string? CameraSerialNumber { get; }

    /// <summary>Gets the name of the camera profile of the file (<c>ProfileName</c>), or <see langword="null"/>.</summary>
    public string? ProfileName { get; }

    /// <summary>
    /// Gets the exposure, in stops, that the rendering adds to give the camera model the same brightness as other models
    /// (<c>BaselineExposure</c>, 0 by default). The decoded pixels include it.
    /// </summary>
    public double BaselineExposure { get; }

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] DngProperties? other)
        => other is not null
            && Version.Equals(other.Version)
            && BackwardVersion.Equals(other.BackwardVersion)
            && string.Equals(UniqueCameraModel, other.UniqueCameraModel, StringComparison.Ordinal)
            && string.Equals(LocalizedCameraModel, other.LocalizedCameraModel, StringComparison.Ordinal)
            && string.Equals(Make, other.Make, StringComparison.Ordinal)
            && string.Equals(Model, other.Model, StringComparison.Ordinal)
            && string.Equals(CameraSerialNumber, other.CameraSerialNumber, StringComparison.Ordinal)
            && string.Equals(ProfileName, other.ProfileName, StringComparison.Ordinal)
            && BaselineExposure.Equals(other.BaselineExposure);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DngProperties);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Version, BackwardVersion, UniqueCameraModel, Make, Model, CameraSerialNumber, ProfileName, BaselineExposure);

    /// <inheritdoc />
    public override string ToString() => $"DNG {Version}{(UniqueCameraModel is null ? "" : ", " + UniqueCameraModel)}";
}
