using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The SHA-256 and length of a preserved payload.</summary>
public sealed class ProfileExpectation
{
    /// <summary>Gets the lowercase hexadecimal SHA-256 of the payload.</summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>Gets the payload length in bytes.</summary>
    [JsonPropertyName("length")]
    public required int Length { get; init; }

    /// <summary>Determines whether a payload matches this expectation.</summary>
    /// <param name="data">The payload.</param>
    /// <returns><see langword="true"/> if the length and the hash match.</returns>
    public bool Matches(ReadOnlySpan<byte> data)
        => data.Length == Length && string.Equals(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data)), Sha256, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Length} bytes, sha256 {Sha256}");
}
