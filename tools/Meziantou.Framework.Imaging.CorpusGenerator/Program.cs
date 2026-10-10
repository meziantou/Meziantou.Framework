using Meziantou.Framework.Imaging.CorpusGenerator;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

const string Usage = """
    Generates the golden corpus of Meziantou.Framework.Imaging (tests/Meziantou.Framework.Imaging.Fixtures).

    This tool is the reviewed, reproducible recipe of every committed fixture. It is NEVER run by the tests and does not
    use the library under test. See tests/Meziantou.Framework.Imaging.Fixtures/README.md ("Regenerating the corpus").

    Usage (from the repository root):
        dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- <generator> [options]

    Generators:
        golden        PNG, APNG, GIF and JPEG fixtures (macOS: ffmpeg, cjpeg/djpeg, sips, swift)
        webp          WebP fixtures (cwebp, dwebp, webpmux, anim_dump, ffmpeg; any OS)
        qoi           QOI fixtures (the qoi.h reference implementation, cc, ffmpeg; any OS)
        bmp           BMP fixtures (ffmpeg; any OS)
        tga           TGA fixtures (ffmpeg; any OS)
        pnm           Netpbm fixtures (ffmpeg; any OS)

    Verification (read-only, any tool versions):
        verify        re-decode the committed corpus with the ffmpeg, dwebp and anim_dump of the pinned meziantou/prebuilt release and
                      compare with the committed references: detects reference drift and the impact of a tool upgrade

    Options:
        --write                   replace the committed fixtures of the generator (explicit, reviewed operation);
                                  without it, regenerate in a temporary directory and diff
        --accept-tool-versions    allow tool versions other than the pinned ones
        --qoi-header <path>       path of the pinned qoi.h reference implementation (qoi only, required)
        --output <directory>      generate into this new or empty directory and keep it (to inspect the output)
    """;

string? generator = null;
var write = false;
var acceptToolVersions = false;
string? qoiHeader = null;
string? output = null;
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
        case "--accept-tool-versions":
            acceptToolVersions = true;
            break;
        case "--qoi-header" when i + 1 < args.Length:
            qoiHeader = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case var value when generator is null && !value.StartsWith('-', StringComparison.Ordinal):
            generator = value;
            break;
        default:
            Console.Error.WriteLine("Unexpected argument: " + args[i]);
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

var options = new GeneratorOptions(write, acceptToolVersions, qoiHeader, output);
try
{
    switch (generator)
    {
        case "golden":
            return GoldenCorpus.Run(options);
        case "webp":
            return WebPCorpus.Run(options);
        case "qoi":
            if (qoiHeader is null)
            {
                Console.Error.WriteLine("The qoi generator requires --qoi-header <path of qoi.h>.");
                return 2;
            }

            return QoiCorpus.Run(options);
        case "bmp":
            return BmpCorpus.Run(options);
        case "tga":
            return TgaCorpus.Run(options);
        case "pnm":
            return PnmCorpus.Run(options);
        case "verify":
            return CorpusVerifier.Run();
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (FatalException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
