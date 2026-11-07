namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The sample format that the configuration record of a coded image or of a track declares.</summary>
internal readonly record struct HeifSampleFormat(int BitDepth, bool IsMonochrome, int SubsamplingX, int SubsamplingY);
