#!/usr/bin/env dotnet
#:sdk Meziantou.NET.Sdk
#:package Meziantou.Prebuilt@3.11.0
// Writes the fonts the renderer of Meziantou.Framework.Pdf bundles to replace the standard fonts that a document does not
// embed: the Liberation fonts (SIL Open Font License 1.1), whose metrics are those of Arial, Times New Roman and Courier
// New, subset to the characters of the Latin encodings of PDF (hb-subset), without hinting, as WOFF2 files.
// The release is pinned by its digest: the output only changes with this script.
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Prebuilt;

const string Url = "https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz";
const string Sha256 = "7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0";

// The characters of StandardEncoding, WinAnsiEncoding, MacRomanEncoding and PDFDocEncoding (ISO 32000-2, annex D)
const string Unicodes = "20-7E,A0-17F,192,1FA-1FF,218-21B,2C6-2C7,2C9,2D8-2DD,2013-2015,2017-201E,2020-2022,2026,2030,2032-2033,2039-203A,2044,20AC,2122,2212,2215,FB01-FB02";

var root = FindRoot(Environment.CurrentDirectory) ?? FindRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Cannot find the root of the repository (Meziantou.Framework.slnx)");
var output = Path.Combine(root, "src", "Meziantou.Framework.Pdf", "Rendering", "Fonts", "Substitution");
var work = Path.Combine(Path.GetTempPath(), "pdf-substitution-fonts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    using var http = new HttpClient();
    var archive = await http.GetByteArrayAsync(Url);
    if (!Convert.ToHexStringLower(SHA256.HashData(archive)).Equals(Sha256, StringComparison.Ordinal))
        throw new InvalidOperationException("The digest of the release does not match");

    var hbSubset = await PrebuiltTools.HbSubset.GetOrDownloadAsync(CancellationToken.None);
    var woff2Compress = await PrebuiltTools.Woff2Compress.GetOrDownloadAsync(CancellationToken.None);
    Directory.CreateDirectory(output);
    using var reader = new TarReader(new GZipStream(new MemoryStream(archive), CompressionMode.Decompress));
    while (reader.GetNextEntry() is { } entry)
    {
        var name = Path.GetFileName(entry.Name);
        if (entry.DataStream is null || !(name.EndsWith(".ttf", StringComparison.Ordinal) || name == "LICENSE"))
            continue;

        if (name == "LICENSE")
        {
            entry.ExtractToFile(Path.Combine(output, "OFL.txt"), overwrite: true);
            continue;
        }

        var source = Path.Combine(work, name);
        var subset = Path.Combine(work, "subset-" + name);
        entry.ExtractToFile(source, overwrite: true);
        Run(hbSubset, "--unicodes=" + Unicodes, "--no-hinting", "--drop-tables+=GSUB,GPOS,GDEF,kern,DSIG,gasp,FFTM", "--output-file=" + subset, source);
        Run(woff2Compress, subset);
        File.Copy(Path.ChangeExtension(subset, ".woff2"), Path.Combine(output, Path.ChangeExtension(name, ".woff2")), overwrite: true);
    }
}
finally
{
    Directory.Delete(work, recursive: true);
}

return 0;

static void Run(string fileName, params string[] arguments)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false }) ?? throw new InvalidOperationException("Cannot start " + fileName);
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with the exit code {process.ExitCode}");
}

static string? FindRoot(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Meziantou.Framework.slnx")))
            return current.FullName;
    }

    return null;
}
