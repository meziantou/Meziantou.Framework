namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A corpus input used as a fuzz seed.</summary>
/// <param name="Id">The identifier of the input.</param>
/// <param name="Data">The input, as the public API takes it.</param>
/// <param name="HevcStream">The raw HEVC byte stream that <paramref name="Data"/> wraps, for the mutations that change the structure of the stream before it is wrapped; otherwise <see langword="null"/>.</param>
internal sealed record FuzzSeed(string Id, byte[] Data, byte[]? HevcStream = null);
