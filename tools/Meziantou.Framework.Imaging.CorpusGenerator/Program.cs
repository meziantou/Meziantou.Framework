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
        avif          AV1 conformance vectors, intra, inter and layered, with plane digests (the ffmpeg of the pinned
                      meziantou/prebuilt release: libaom and SVT-AV1 encoders, dav1d and libaom decoders, trace_headers;
                      libavif avifenc for the streams with spatial layers)
        avif-container
                      AVIF fixtures: stills, grids, alpha, transformations, metadata, image sequences, layered images,
                      malformed and over-limit files (libavif avifenc/avifdec and the ffmpeg of the pinned
                      meziantou/prebuilt release)
        hevc          HEVC conformance vectors, intra and inter, with plane digests (the tools of the pinned
                      meziantou/prebuilt release: x265 and kvazaar through ffmpeg and HM TAppEncoder as encoders; the
                      hevc decoder of ffmpeg, libde265 dec265 and HM TAppDecoder as decoders; trace_headers)
        heic-container
                      HEIC fixtures: stills, grids, alpha, transformations, metadata, image sequences, configuration
                      records, malformed and over-limit files (libheif heif-enc/heif-dec, x265 through ffmpeg, and the
                      three HEVC decoders of the pinned meziantou/prebuilt release)
        jxl           JPEG XL fixtures: lossless and lossy stills, metadata, containers, animations, malformed, unsupported
                      and over-limit files (cjxl, jxl_from_tree, djxl and jxl-oxide of the pinned meziantou/prebuilt release)
        icc           ICC profiles and color conversion vectors (transicc of LittleCMS; any OS)
        icc-builtin   the source of the CMYK profile provided by the library, transcribed from the committed corpus
                      (no tool; any OS)
        fonts         font files and the values independent font readers give for them: synthetic fonts built by
                      Common/FontBuilder.cs, third-party fonts subset by hb-subset, their WOFF and WOFF2 forms
                      (the HarfBuzz utilities and the woff2 tools of the pinned meziantou/prebuilt release; any OS)
        language-tags the source of the table of the language system tags of OpenType the library has, transcribed
                      from the registry of the OpenType specification, the code list of ISO 639-2 and the language
                      subtag registry of BCP 47 (no tool; any OS)
        arabic-forms  the source of the table of the presentation forms of the Arabic letters the library has, transcribed
                      from the Unicode Character Database (no tool; any OS)

    Verification (read-only, any tool versions):
        verify        re-decode the committed corpus with the ffmpeg, dwebp and anim_dump of the pinned meziantou/prebuilt release and
                      compare with the committed references: detects reference drift and the impact of a tool upgrade. The AV1
                      vectors are checked too: file hashes, and plane digests re-derived with dav1d and libaom; and the HEVC
                      vectors: file hashes, and plane digests re-derived with ffmpeg, dec265 and TAppDecoder

    Options:
        --write                   replace the committed fixtures of the generator (explicit, reviewed operation);
                                  without it, regenerate in a temporary directory and diff
        --accept-tool-versions    allow tool versions other than the pinned ones
        --qoi-header <path>       path of the pinned qoi.h reference implementation (qoi only, required)
        --icc-profiles <dir>      profiles directory of the pinned checkout of saucecontrol/Compact-ICC-Profiles
                                  (icc only, required)
        --fonts <dir>             directory of the pinned third-party font files, as downloaded from the sources
                                  FontCorpus.cs lists (fonts only, required)
        --language-registry <file>
                                  the page "Language system tags" of the OpenType specification, saved as HTML
                                  (language-tags only, required)
        --iso-639 <file>          the code list ISO-639-2_utf-8.txt of the Library of Congress (language-tags only,
                                  required)
        --language-subtags <file> the language subtag registry of IANA (language-tags only, required)
        --unicode-data <file>     the file UnicodeData.txt of the pinned version of Unicode (arabic-forms only, required)
        --output <directory>      generate into this new or empty directory and keep it (to inspect the output)
    """;

string? generator = null;
var write = false;
var acceptToolVersions = false;
string? qoiHeader = null;
string? iccProfiles = null;
string? fonts = null;
string? languageRegistry = null;
string? iso639 = null;
string? languageSubtags = null;
string? unicodeData = null;
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
        case "--icc-profiles" when i + 1 < args.Length:
            iccProfiles = args[++i];
            break;
        case "--fonts" when i + 1 < args.Length:
            fonts = args[++i];
            break;
        case "--language-registry" when i + 1 < args.Length:
            languageRegistry = args[++i];
            break;
        case "--iso-639" when i + 1 < args.Length:
            iso639 = args[++i];
            break;
        case "--language-subtags" when i + 1 < args.Length:
            languageSubtags = args[++i];
            break;
        case "--unicode-data" when i + 1 < args.Length:
            unicodeData = args[++i];
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

var options = new GeneratorOptions(write, acceptToolVersions, qoiHeader, output, iccProfiles, fonts, languageRegistry, iso639, languageSubtags, unicodeData);
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
        case "avif":
            return AvifCorpus.Run(options);
        case "avif-container":
            return AvifCorpus.RunContainer(options);
        case "hevc":
            return HevcCorpus.Run(options);
        case "heic-container":
            return HeicCorpus.RunContainer(options);
        case "jxl":
            return JxlCorpus.Run(options);
        case "icc":
            if (iccProfiles is null)
            {
                Console.Error.WriteLine("The icc generator requires --icc-profiles <profiles directory of Compact-ICC-Profiles>.");
                return 2;
            }

            return IccCorpus.Run(options);
        case "icc-builtin":
            return IccCorpus.RunBuiltInProfile(options);
        case "fonts":
            if (fonts is null)
            {
                Console.Error.WriteLine("The fonts generator requires --fonts <directory of the pinned third-party font files>.");
                return 2;
            }

            return FontCorpus.Run(options);
        case "language-tags":
            if (languageRegistry is null || iso639 is null || languageSubtags is null)
            {
                Console.Error.WriteLine("The language-tags generator requires --language-registry <saved page of the registry>, --iso-639 <code list of ISO 639-2> and --language-subtags <language subtag registry>.");
                return 2;
            }

            return LanguageTagTable.Run(options);
        case "arabic-forms":
            if (unicodeData is null)
            {
                Console.Error.WriteLine("The arabic-forms generator requires --unicode-data <UnicodeData.txt of the pinned version of Unicode>.");
                return 2;
            }

            return ArabicFormTable.Run(options);
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
