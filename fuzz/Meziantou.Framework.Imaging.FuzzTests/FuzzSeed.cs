namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A corpus input used as a fuzz seed.</summary>
internal sealed record FuzzSeed(string Id, byte[] Data);
