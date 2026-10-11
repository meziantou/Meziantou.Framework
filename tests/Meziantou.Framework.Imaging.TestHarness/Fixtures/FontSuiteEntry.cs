using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A third-party suite of test fonts and expectations, stored as published: a directory of the corpus that is one
/// entry of the manifest, identified by the number of its files and by a hash of their names and contents.
/// </summary>
public sealed class FontSuiteEntry
{
    /// <summary>Unique identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The directory of the suite, relative to the corpus root.</summary>
    [JsonPropertyName("directory")]
    public required string Directory { get; init; }

    /// <summary>Where the suite comes from and its license.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>The number of files of the directory.</summary>
    [JsonPropertyName("fileCount")]
    public required int FileCount { get; init; }

    /// <summary>
    /// The SHA-256 of the list of the files: for each file, by ordinal order of its path relative to the directory, the
    /// path, a line feed, the lowercase hexadecimal SHA-256 of its content, a line feed.
    /// </summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>What the suite is.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Computes the hash of the files of a directory, as <see cref="Sha256"/> defines it.</summary>
    public static string ComputeHash(FullPath directory, out int fileCount)
    {
        var files = System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => FullPath.FromPath(file).MakePathRelativeTo(directory).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        fileCount = files.Count;
        var listing = new StringBuilder();
        foreach (var file in files)
        {
            listing.Append(file).Append('\n').Append(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(directory / file)))).Append('\n');
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(listing.ToString())));
    }
}
