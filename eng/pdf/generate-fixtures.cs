#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:project ../../tests/Meziantou.Framework.Pdf.TestHarness/Meziantou.Framework.Pdf.TestHarness.csproj
// Generates tests/Meziantou.Framework.Pdf.Fixtures from its sources: the hand-authored templates of sources/ are
// expanded (offsets, lengths, cross-reference tables), qpdf writes the variants of the valid files (object streams,
// linearization, encryption), and manifest.json records the hash, the provenance and the expectations of every file,
// with the hashes of the vendored Arlington PDF Model.
//
// dotnet run eng/pdf/generate-fixtures.cs              regenerate in a temporary directory and compare
// dotnet run eng/pdf/generate-fixtures.cs -- --write   replace the fixtures and the manifest
using System.Collections.ObjectModel;
using Meziantou.Framework;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;
using Meziantou.Framework.Pdf.TestHarness.Templates;

var write = args.Contains("--write", StringComparer.Ordinal);
var fixturesRoot = RepositoryPaths.Fixtures;
var outputRoot = write ? fixturesRoot : FullPath.GetTempPath() / ("pdf-fixtures-" + Guid.NewGuid().ToString("N"));
var qpdf = await QpdfTool.LocateAsync(CancellationToken.None);

const string Author = "Meziantou.Framework.Pdf contributors";
const string ArlingtonCommit = "d148fee5a0cb6dcacadf58c09ed842e32847024d";

var fontsText = "Caf\u00E9 \u20AC \u2013 \u201Cquoted\u201D\nIt\u2019s \u2018quoted\u2019\n\u03B1\u03B2\u03B3 \u2248 \u03C0\n\u00C5\u00E7\uFB01\uFB02\nK\u00E9\u03B1\u03B2\u03B3\n\u00E4\u00E9\u00DF";
Recipe[] recipes =
[
    new("simple", "valid", ["xref.table", "font.standard14"], new() { Pages = 1, Version = "1.7", Repaired = false, Text = ["Hello, World"] }),
    new("multipage", "valid", ["xref.table", "pagetree.nested", "pagetree.inheritance", "contents.array"], new() { Pages = 3, Repaired = false, Text = ["Page 1", "Page 2", "Page 3"] }),
    new("fonts", "valid", ["xref.table", "font.standard14", "font.encoding.differences", "font.type0", "tounicode"], new() { Pages = 1, Repaired = false, Text = [fontsText] }),
    new("filters", "valid", ["xref.table", "filter.asciihex", "filter.ascii85", "filter.runlength", "filter.chain", "contents.array"], new() { Pages = 1, Repaired = false, Text = ["Hex\nAscii85\nRunLength"] }),
    new("navigation", "valid", ["xref.table", "outlines", "links", "dests.dictionary", "dests.nametree", "annotations"], new() { Pages = 2, Repaired = false }),
    new("metadata", "valid", ["xref.table", "info", "xmp", "textstring.utf16"], new() { Pages = 1, Repaired = false, Title = "M\u00E9tadata" }),
    new("incremental", "valid", ["xref.table", "incremental"], new() { Pages = 1, Repaired = false, Text = ["Updated"], Title = "Second revision" }),
    new("damaged-offsets", "damaged", ["xref.table", "xref.wrongoffsets"], new() { Pages = 1, Text = ["Recovered"] }),
    new("damaged-structure", "damaged", ["xref.missing", "trailer.missing", "endobj.missing", "stream.wronglength"], new() { Pages = 1, Repaired = true, Text = ["Damaged"] }),
    new("invalid-no-header", "invalid", ["header.missing"], new() { Exception = "InvalidPdfContentException" }),
];

(string Suffix, string Feature, string[] Arguments)[] variants =
[
    ("objstm", "objstm", ["--object-streams=generate", "--compress-streams=y", "--deterministic-id"]),
    ("linearized", "linearized", ["--linearize", "--deterministic-id"]),
];

(string Name, string Feature, string? Password, string[] Arguments)[] encryptions =
[
    ("rc4-40", "encryption.r2", null, ["--allow-weak-crypto", "--encrypt", "", "owner", "40", "--"]),
    ("rc4-128", "encryption.r3", null, ["--allow-weak-crypto", "--encrypt", "", "owner", "128", "--use-aes=n", "--"]),
    ("aes-128", "encryption.r4", null, ["--encrypt", "", "owner", "128", "--use-aes=y", "--"]),
    ("aes-256", "encryption.r6", null, ["--encrypt", "", "owner", "256", "--"]),
    ("aes-256-user-password", "encryption.r6", "user", ["--encrypt", "user", "owner", "256", "--"]),
];

var manifest = new PdfFixtureManifest
{
    Generator = new PdfFixtureGenerator
    {
        Script = "eng/pdf/generate-fixtures.cs",
        Command = "dotnet run eng/pdf/generate-fixtures.cs -- --write",
        Tools = ["qpdf " + QpdfTool.Version],
    },
};

foreach (var recipe in recipes)
{
    var directory = recipe.Kind switch
    {
        "valid" => "structure",
        "damaged" => "damaged",
        _ => "invalid",
    };
    var relativePath = directory + "/" + recipe.Name + ".pdf";
    var path = outputRoot / relativePath;
    Directory.CreateDirectory(path.Parent);
    await File.WriteAllBytesAsync(path, PdfTemplate.ExpandFile(fixturesRoot / "sources" / (recipe.Name + ".in")));
    manifest.Fixtures.Add(new PdfFixture
    {
        Path = relativePath,
        Sha256 = PdfFixtureManifest.ComputeSha256(path),
        Kind = recipe.Kind,
        Provenance = new PdfFixtureProvenance { Origin = "hand-authored", License = "CC0-1.0", Author = Author, Source = "sources/" + recipe.Name + ".in" },
        Features = [.. recipe.Features, "template"],
        Expected = recipe.Expected,
    });

    if (recipe.Kind != "valid")
        continue;

    foreach (var (suffix, feature, arguments) in variants)
    {
        var variantPath = directory + "/" + recipe.Name + "." + suffix + ".pdf";
        await qpdf.RewriteAsync(path, outputRoot / variantPath, arguments, CancellationToken.None);
        manifest.Fixtures.Add(new PdfFixture
        {
            Path = variantPath,
            Sha256 = PdfFixtureManifest.ComputeSha256(outputRoot / variantPath),
            Kind = "valid",
            Provenance = new PdfFixtureProvenance { Origin = "tool", License = "CC0-1.0", Author = Author, Source = relativePath, Tool = "qpdf " + QpdfTool.Version + " " + string.Join(' ', arguments) },
            Features = [.. recipe.Features.Where(f => f is not ("xref.table" or "incremental")), feature, feature == "objstm" ? "xref.stream" : "xref.table"],
            Expected = new PdfFixtureExpectation { Pages = recipe.Expected.Pages, Repaired = false, Text = recipe.Expected.Text, Title = recipe.Expected.Title },
        });
    }
}

var simple = recipes.Single(recipe => recipe.Name == "simple");
foreach (var (name, feature, password, arguments) in encryptions)
{
    var relativePath = "encryption/simple-" + name + ".pdf";
    Directory.CreateDirectory((outputRoot / relativePath).Parent);
    string[] allArguments = ["--static-id", "--static-aes-iv", .. arguments];
    await qpdf.RewriteAsync(outputRoot / "structure/simple.pdf", outputRoot / relativePath, allArguments, CancellationToken.None);
    manifest.Fixtures.Add(new PdfFixture
    {
        Path = relativePath,
        Sha256 = PdfFixtureManifest.ComputeSha256(outputRoot / relativePath),
        Kind = "encrypted",
        Provenance = new PdfFixtureProvenance { Origin = "tool", License = "CC0-1.0", Author = Author, Source = "structure/simple.pdf", Tool = "qpdf " + QpdfTool.Version + " " + string.Join(' ', allArguments) },
        Features = ["encryption", feature],
        Expected = new PdfFixtureExpectation { Pages = simple.Expected.Pages, Repaired = false, Text = simple.Expected.Text, Password = password },
    });
}

var arlington = new PdfVendoredDataSet
{
    Path = "arlington/tsv/latest",
    Source = "https://github.com/pdf-association/arlington-pdf-model/tree/" + ArlingtonCommit + "/tsv/latest",
    Commit = ArlingtonCommit,
    License = "Apache-2.0",
    LicenseFile = "arlington/LICENSE",
};
foreach (var file in Directory.EnumerateFiles(fixturesRoot / arlington.Path, "*.tsv").Order(StringComparer.Ordinal))
{
    arlington.Files[Path.GetFileName(file)] = PdfFixtureManifest.ComputeSha256(file);
}

manifest.Vendored.Add(arlington);
manifest.Save(outputRoot / "manifest.json");

if (write)
{
    Console.WriteLine($"Wrote {manifest.Fixtures.Count} fixtures and the manifest");
    return 0;
}

// Compare the regenerated files with the committed ones. qpdf draws the salts of the AES-256 keys (revision 6) at
// random whatever its options: these files are only checked to exist.
var differences = 0;
var committedManifest = PdfFixtureManifest.Load(fixturesRoot / "manifest.json");
foreach (var fixture in manifest.Fixtures)
{
    var committed = fixturesRoot / fixture.Path;
    if (!File.Exists(committed))
    {
        Console.WriteLine($"missing: {fixture.Path}");
        differences++;
    }
    else if (fixture.Features.Contains("encryption.r6", StringComparer.Ordinal))
    {
        fixture.Sha256 = committedManifest.Fixtures.FirstOrDefault(f => f.Path == fixture.Path)?.Sha256 ?? fixture.Sha256;
    }
    else if (PdfFixtureManifest.ComputeSha256(committed) != fixture.Sha256)
    {
        Console.WriteLine($"differs: {fixture.Path}");
        differences++;
    }
}

manifest.Save(outputRoot / "manifest.json");
if (!File.ReadAllText(fixturesRoot / "manifest.json").Equals(File.ReadAllText(outputRoot / "manifest.json"), StringComparison.Ordinal))
{
    Console.WriteLine("differs: manifest.json");
    differences++;
}

Directory.Delete(outputRoot, recursive: true);
Console.WriteLine(differences == 0 ? "The fixtures are up to date" : $"{differences} differences: run with --write to replace the fixtures");
return differences == 0 ? 0 : 1;

internal sealed record Recipe(string Name, string Kind, string[] Features, PdfFixtureExpectation Expected);
