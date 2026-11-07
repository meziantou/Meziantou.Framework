namespace Meziantou.Framework.Fuzzing;

/// <summary>An input of the initial corpus of a target.</summary>
/// <param name="Name">A short name of the input, used for the file name of the seed.</param>
/// <param name="Data">The input.</param>
public sealed record FuzzSeed(string Name, ReadOnlyMemory<byte> Data)
{
    /// <summary>Creates a seed from a text, encoded so that <see cref="FuzzText.Decode"/> returns it unchanged.</summary>
    public static FuzzSeed FromText(string name, string text) => new(name, FuzzText.Encode(text));

    /// <summary>Creates a seed made of option bytes followed by a text (<see cref="FuzzInput.ReadByte"/>, then <see cref="FuzzInput.ReadText"/>).</summary>
    public static FuzzSeed FromText(string name, ReadOnlySpan<byte> prefix, string text) => new(name, (byte[])[.. prefix, .. FuzzText.Encode(text)]);
}
