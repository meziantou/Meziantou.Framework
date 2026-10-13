namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The light level of the content (Rec. ITU-T H.265, clause D.2.35 "Content light level information SEI message syntax";
/// CTA-861.3).
/// </summary>
/// <param name="MaxContentLightLevel"><c>max_content_light_level</c>, in candelas per square metre.</param>
/// <param name="MaxPicAverageLightLevel"><c>max_pic_average_light_level</c>.</param>
internal sealed record HevcContentLightLevel(ushort MaxContentLightLevel, ushort MaxPicAverageLightLevel);
