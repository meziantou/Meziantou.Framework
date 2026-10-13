namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// How the samples of an auxiliary picture are interpreted as alpha values (Rec. ITU-T H.265, clause F.14.2.8 "Alpha
/// channel information SEI message syntax").
/// </summary>
/// <param name="CancelFlag"><c>alpha_channel_cancel_flag</c>: the message cancels the previous one, and has no other value.</param>
/// <param name="UseIdc"><c>alpha_channel_use_idc</c>: 0 when the decoded samples are to be multiplied by the alpha values, 1 when they are not, 2 when the use is unspecified.</param>
/// <param name="BitDepth"><c>alpha_channel_bit_depth_minus8 + 8</c>.</param>
/// <param name="TransparentValue"><c>alpha_transparent_value</c>.</param>
/// <param name="OpaqueValue"><c>alpha_opaque_value</c>.</param>
/// <param name="IncrFlag"><c>alpha_channel_incr_flag</c>.</param>
/// <param name="ClipFlag"><c>alpha_channel_clip_flag</c>.</param>
/// <param name="ClipTypeFlag"><c>alpha_channel_clip_type_flag</c>.</param>
internal sealed record HevcAlphaChannelInfo(bool CancelFlag, int UseIdc, int BitDepth, int TransparentValue, int OpaqueValue, bool IncrFlag, bool ClipFlag, bool ClipTypeFlag);
