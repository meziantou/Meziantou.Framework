namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The colour volume of the display the content was mastered on (Rec. ITU-T H.265, clause D.2.28 "Mastering display colour
/// volume SEI message syntax"; SMPTE ST 2086).
/// </summary>
/// <param name="DisplayPrimariesX0"><c>display_primaries_x[ 0 ]</c>, in increments of 0.00002.</param>
/// <param name="DisplayPrimariesY0"><c>display_primaries_y[ 0 ]</c>.</param>
/// <param name="DisplayPrimariesX1"><c>display_primaries_x[ 1 ]</c>.</param>
/// <param name="DisplayPrimariesY1"><c>display_primaries_y[ 1 ]</c>.</param>
/// <param name="DisplayPrimariesX2"><c>display_primaries_x[ 2 ]</c>.</param>
/// <param name="DisplayPrimariesY2"><c>display_primaries_y[ 2 ]</c>.</param>
/// <param name="WhitePointX"><c>white_point_x</c>.</param>
/// <param name="WhitePointY"><c>white_point_y</c>.</param>
/// <param name="MaxDisplayMasteringLuminance"><c>max_display_mastering_luminance</c>, in units of 0.0001 candelas per square metre.</param>
/// <param name="MinDisplayMasteringLuminance"><c>min_display_mastering_luminance</c>.</param>
internal sealed record HevcMasteringDisplayColourVolume(
    ushort DisplayPrimariesX0,
    ushort DisplayPrimariesY0,
    ushort DisplayPrimariesX1,
    ushort DisplayPrimariesY1,
    ushort DisplayPrimariesX2,
    ushort DisplayPrimariesY2,
    ushort WhitePointX,
    ushort WhitePointY,
    uint MaxDisplayMasteringLuminance,
    uint MinDisplayMasteringLuminance);
