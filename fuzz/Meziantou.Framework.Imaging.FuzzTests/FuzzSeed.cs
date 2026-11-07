namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A corpus input used as a fuzz seed.</summary>
/// <param name="Id">The identifier of the input.</param>
/// <param name="Data">The input, as the public API takes it.</param>
/// <param name="HevcStream">The raw HEVC byte stream that <paramref name="Data"/> wraps, for the mutations that change the structure of the stream before it is wrapped; otherwise <see langword="null"/>.</param>
/// <param name="Av1Stream">The raw AV1 stream that <paramref name="Data"/> wraps, for the mutations that change its units before it is wrapped again (<see cref="FuzzAv1Mutator"/>); otherwise <see langword="null"/>.</param>
internal sealed record FuzzSeed(string Id, byte[] Data, byte[]? HevcStream = null, FuzzAv1Stream? Av1Stream = null);
