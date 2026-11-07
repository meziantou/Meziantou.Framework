namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>What an <see cref="HevcReferenceDecoder"/> gives for a stream.</summary>
/// <param name="Planes">The raw planes of every output picture; empty when the decoder gives none.</param>
/// <param name="Error">Why the decoder gives no planes, or <see langword="null"/>.</param>
public sealed record HevcReferenceDecoderResult(ReadOnlyMemory<byte> Planes, string? Error)
{
    /// <summary>Gets whether the decoder decoded the stream.</summary>
    public bool Succeeded => Error is null;

    /// <summary>
    /// Gets what the decoder printed on its standard output; empty when it was not started or did not finish.
    /// <c>TAppDecoder</c> prints a line per picture there, which says whether the picture has the hash that the stream
    /// carries for it (<c>[MD5:...,(OK)]</c>).
    /// </summary>
    public string StandardOutput { get; init; } = "";

    /// <summary>Creates the result of a decoder that rejected a stream, crashed or did not finish.</summary>
    /// <param name="error">The reason.</param>
    /// <param name="standardOutput">What the decoder printed on its standard output.</param>
    /// <returns>The result.</returns>
    public static HevcReferenceDecoderResult Failure(string error, string standardOutput = "") => new(default, error) { StandardOutput = standardOutput };
}
