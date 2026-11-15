namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the Adobe Digital Negative format (DNG specification 1.7.1.0, September 2023): the tags of chapter 4,
/// the values of NewSubFileType (chapter 3), and the exceptions of the format.
/// </summary>
internal static class DngFormat
{
    /// <summary>The version of the DNG specification this reader implements: files whose DNGBackwardVersion is newer are rejected.</summary>
    public static ReadOnlySpan<byte> SupportedVersion => [1, 7, 1, 0];

    // TIFF/EP and EXIF tags DNG uses
    public const ushort MakeTag = 271;
    public const ushort ModelTag = 272;
    public const ushort CfaRepeatPatternDimTag = 33421;
    public const ushort CfaPatternTag = 33422;

    // DNG tags (chapter 4)
    public const ushort DngVersionTag = 50706;
    public const ushort DngBackwardVersionTag = 50707;
    public const ushort UniqueCameraModelTag = 50708;
    public const ushort LocalizedCameraModelTag = 50709;
    public const ushort CfaPlaneColorTag = 50710;
    public const ushort CfaLayoutTag = 50711;
    public const ushort LinearizationTableTag = 50712;
    public const ushort BlackLevelRepeatDimTag = 50713;
    public const ushort BlackLevelTag = 50714;
    public const ushort BlackLevelDeltaHTag = 50715;
    public const ushort BlackLevelDeltaVTag = 50716;
    public const ushort WhiteLevelTag = 50717;
    public const ushort DefaultScaleTag = 50718;
    public const ushort DefaultCropOriginTag = 50719;
    public const ushort DefaultCropSizeTag = 50720;
    public const ushort ColorMatrix1Tag = 50721;
    public const ushort ColorMatrix2Tag = 50722;
    public const ushort CameraCalibration1Tag = 50723;
    public const ushort CameraCalibration2Tag = 50724;
    public const ushort ReductionMatrix1Tag = 50725;
    public const ushort ReductionMatrix2Tag = 50726;
    public const ushort AnalogBalanceTag = 50727;
    public const ushort AsShotNeutralTag = 50728;
    public const ushort AsShotWhiteXyTag = 50729;
    public const ushort BaselineExposureTag = 50730;
    public const ushort CameraSerialNumberTag = 50735;
    public const ushort CalibrationIlluminant1Tag = 50778;
    public const ushort CalibrationIlluminant2Tag = 50779;
    public const ushort ActiveAreaTag = 50829;
    public const ushort AsShotIccProfileTag = 50831;
    public const ushort CameraCalibrationSignatureTag = 50931;
    public const ushort ProfileCalibrationSignatureTag = 50932;
    public const ushort ProfileNameTag = 50936;
    public const ushort ProfileToneCurveTag = 50940;
    public const ushort ForwardMatrix1Tag = 50964;
    public const ushort ForwardMatrix2Tag = 50965;
    public const ushort PreviewColorSpaceTag = 50970;
    public const ushort OpcodeList1Tag = 51008;
    public const ushort OpcodeList2Tag = 51009;
    public const ushort OpcodeList3Tag = 51022;
    public const ushort BaselineExposureOffsetTag = 51109;
    public const ushort DefaultBlackRenderTag = 51110;
    public const ushort CalibrationIlluminant3Tag = 52529;
    public const ushort ColumnInterleaveFactorTag = 52547;
    public const ushort RowInterleaveFactorTag = 50975;
    public const ushort SubTileBlockSizeTag = 50974;

    // NewSubFileType values (chapter 3)
    public const uint SubfileMain = 0;
    public const uint SubfilePreview = 1;
    public const uint SubfileTransparency = 4;
    public const uint SubfilePreviewTransparency = 5;
    public const uint SubfileDepth = 8;
    public const uint SubfileReducedDepth = 9;
    public const uint SubfileEnhanced = 16;
    public const uint SubfileAlternatePreview = 0x10001;
    public const uint SubfileSemanticMask = 0x10004;

    /// <summary>The CFALayout of a rectangular (or square) color filter array, the only one supported.</summary>
    public const int CfaLayoutRectangular = 1;

    /// <summary>The largest dimension of the repeating pattern of a color filter array or of the black levels.</summary>
    public const int MaxPatternSize = 16;

    /// <summary>The largest number of image file directories of the SubIFD tree that are walked.</summary>
    public const int MaxDirectories = 64;

    /// <summary>The largest number of entries of a linearization table (the values of 16-bit samples).</summary>
    public const int MaxLinearizationTableLength = 65536;

    /// <summary>Creates the exception reported for malformed DNG data.</summary>
    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Dng);

    /// <summary>Creates the exception reported for malformed DNG data, caused by another one.</summary>
    public static InvalidImageContentException Invalid(string message, Exception innerException) => new(message, ImageFormat.Dng, innerException);

    /// <summary>Creates the exception reported for a recognized but unsupported DNG feature.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Dng, feature);

    /// <summary>Formats a four-tier version number, such as <c>1.4.0.0</c>.</summary>
    public static string FormatVersion(ReadOnlySpan<byte> version) => string.Create(CultureInfo.InvariantCulture, $"{version[0]}.{version[1]}.{version[2]}.{version[3]}");

    /// <summary>Compares two four-tier version numbers.</summary>
    public static int CompareVersions(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceCompareTo(right);
}
