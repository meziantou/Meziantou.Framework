namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An operating point of a sequence header (AV1 specification, section 5.5.1): the layers it decodes and the level it conforms to.</summary>
/// <param name="Idc">The layers of the operating point (<c>operating_point_idc</c>): temporal layers in bits 0-7, spatial layers in bits 8-11.</param>
/// <param name="LevelIndex">The level of the operating point (<c>seq_level_idx</c>).</param>
/// <param name="Tier">The tier of the operating point (<c>seq_tier</c>).</param>
/// <param name="DecoderModelPresent">Whether frame headers carry a buffer removal time for the operating point.</param>
internal readonly record struct Av1OperatingPoint(int Idc, int LevelIndex, int Tier, bool DecoderModelPresent);
