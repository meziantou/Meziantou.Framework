using System.Security.Cryptography;
using Meziantou.Framework;
using Meziantou.Framework.Imaging.Av1TableExtractor;

const string Usage = """
    Transcribes the constant tables of the AV1 Bitstream & Decoding Process Specification into
    src/Meziantou.Framework.Imaging/Internals/Codecs/Avif/Av1/Tables.

    The specification sources (https://github.com/AOMediaCodec/av1-spec) are not committed. Check out the pinned commit and pass
    its directory: every file read by the tool is verified against a pinned SHA-256.

    Usage (from the repository root):
        dotnet run --project tools/Meziantou.Framework.Imaging.Av1TableExtractor -- --spec <directory> [--write]

    Options:
        --spec <directory>    directory of the specification sources at the pinned commit (required)
        --write               replace the generated files; without it, generate in memory and compare with the committed files
    """;

string? spec = null;
var write = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help":
            Console.WriteLine(Usage);
            return 0;
        case "--write":
            write = true;
            break;
        case "--spec" when i + 1 < args.Length:
            spec = args[++i];
            break;
        default:
            Console.Error.WriteLine("Unexpected argument: " + args[i]);
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (spec is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var root = FullPath.CurrentDirectory();
    if (!File.Exists(root / "Meziantou.Framework.slnx"))
        throw new FatalException("Run the tool from the repository root.");

    var specDirectory = FullPath.FromPath(spec);
    var sources = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (name, expectedHash) in PinnedSpecification.Files)
    {
        var path = specDirectory / name;
        if (!File.Exists(path))
            throw new FatalException($"Specification file not found: {path}");

        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
            throw new FatalException($"{name} has SHA-256 {hash}, expected {expectedHash} (commit {PinnedSpecification.Commit}).");

        sources.Add(name, File.ReadAllText(path));
    }

    var specification = SpecificationParser.Parse(sources);
    var files = CSharpEmitter.Emit(specification);

    var outputDirectory = root / "src" / "Meziantou.Framework.Imaging" / "Internals" / "Codecs" / "Avif" / "Av1" / "Tables";
    var existing = Directory.Exists(outputDirectory)
        ? Directory.GetFiles(outputDirectory, "*.g.cs").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
        : [];

    var differences = new List<string>();
    foreach (var (name, content) in files)
    {
        var path = outputDirectory / name;
        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            differences.Add(name);

        existing.Remove(name);
    }

    differences.AddRange(existing.Select(name => name + " (no longer generated)"));

    Console.WriteLine($"{specification.Tables.Count} tables, {specification.Constants.Count} constants, {files.Count} files.");
    if (differences.Count is 0)
    {
        Console.WriteLine("The generated files are up to date.");
        return 0;
    }

    if (!write)
    {
        Console.Error.WriteLine("The generated files differ from the specification (use --write to replace them):");
        foreach (var difference in differences)
        {
            Console.Error.WriteLine("  " + difference);
        }

        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    foreach (var name in existing)
    {
        File.Delete(outputDirectory / name!);
    }

    foreach (var (name, content) in files)
    {
        File.WriteAllText(outputDirectory / name, content);
    }

    Console.WriteLine($"Wrote {differences.Count} file(s) to {outputDirectory}.");
    return 0;
}
catch (FatalException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
