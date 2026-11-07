namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>A chunk read by <see cref="ReferencePng"/> (CRC already verified).</summary>
/// <param name="Type">The four-letter chunk type.</param>
/// <param name="Data">The chunk data.</param>
public sealed record ReferencePngChunk(string Type, ReadOnlyMemory<byte> Data);
