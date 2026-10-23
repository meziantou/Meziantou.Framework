using System.Collections.Concurrent;

namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>
/// The Arlington PDF Model (pdf-association/arlington-pdf-model, Apache-2.0): one TSV file per kind of object, with
/// the type, the requirements, the possible values and the links of each key. The vendored files are those of
/// <c>tsv/latest</c> at the commit the manifest pins.
/// </summary>
public sealed class ArlingtonModel
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, ArlingtonObjectDefinition?> _definitions = new(StringComparer.Ordinal);

    private ArlingtonModel(string directory)
    {
        _directory = directory;
    }

    /// <summary>Gets the model of the vendored TSV files.</summary>
    public static ArlingtonModel Default { get; } = new(RepositoryPaths.ArlingtonTsv);

    /// <summary>Loads the model of a directory of TSV files.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The model.</returns>
    public static ArlingtonModel Load(string directory) => new(directory);

    /// <summary>Gets the definition of a kind of object, such as <c>Catalog</c> or <c>PageObject</c>.</summary>
    /// <param name="name">The name of the TSV file, without extension.</param>
    /// <returns>The definition, or <see langword="null"/> when the model has no such file.</returns>
    public ArlingtonObjectDefinition? Get(string name)
    {
        return _definitions.GetOrAdd(name, key =>
        {
            var path = Path.Combine(_directory, key + ".tsv");
            return File.Exists(path) ? ArlingtonObjectDefinition.Parse(key, File.ReadAllLines(path)) : null;
        });
    }
}
