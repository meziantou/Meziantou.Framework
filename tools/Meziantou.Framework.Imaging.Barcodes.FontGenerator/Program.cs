using System.Globalization;
using Meziantou.Framework;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes.FontGenerator;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Typography;

// Usage:
//   generate [<directory>]   writes OcrA.ttf and OcrB.ttf (default: src/Meziantou.Framework.Imaging.Barcodes/Fonts)
//   check [<directory>]      fails when the files of the directory differ from a fresh generation
//   specimen <directory>     writes a PNG specimen of each font, for review
//   digitize <fig141.png> <fig142.png> <output.cs>
//                            traces the OCR-B centerlines from the 4:1 illustrations of ANSI X3.49-1975 (pages 54
//                            and 55 of FIPS PUB 32-1 (1975), rendered at 600 dpi) into OcrB.Centerlines.cs
var command = args.Length > 0 ? args[0] : "generate";
var directory = args.Length > 1 ? FullPath.FromPath(args[1]) : FindRepositoryRoot() / "src" / "Meziantou.Framework.Imaging.Barcodes" / "Fonts";
OcrFont[] fonts = [OcrA.Font, OcrB.Font];

switch (command)
{
    case "generate":
        Directory.CreateDirectory(directory);
        foreach (var font in fonts)
        {
            var data = font.Build();
            File.WriteAllBytes(directory / font.FileName, data);
            Console.WriteLine($"{font.FileName}: {font.Glyphs.Count} glyphs, {data.Length} bytes");
        }

        return 0;

    case "check":
        var failed = false;
        foreach (var font in fonts)
        {
            var path = directory / font.FileName;
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(font.Build()))
            {
                Console.Error.WriteLine($"{path} is not up to date: run the generator.");
                failed = true;
            }
        }

        return failed ? 1 : 0;

    case "specimen":
        Directory.CreateDirectory(directory);
        foreach (var font in fonts)
        {
            WriteSpecimen(font, directory / Path.ChangeExtension(font.FileName, ".png"));
        }

        return 0;

    case "digitize":
        var pages = OcrBPages(args[1], args[2]);
        File.WriteAllText(args[3], OcrBDigitizer.Digitize(pages));
        return 0;

    case "contours":
        foreach (var design in OcrB.Font.Glyphs.Concat(OcrA.Font.Glyphs).Where(design => design.CodePoints.Contains(char.ConvertToUtf32(args[1], 0))))
        {
            Console.WriteLine($"{design.Name}: {design.Strokes.Count} strokes, {design.Fills.Count} fills");
            foreach (var contour in OutlineBuilder.Build(design))
            {
                Console.WriteLine($"  {contour.Count} points, x {contour.Min(p => p.X)}..{contour.Max(p => p.X)}, y {contour.Min(p => p.Y)}..{contour.Max(p => p.Y)}");
            }
        }

        return 0;

    case "review":
        OcrBDigitizer.WriteReview(OcrBPages(args[1], args[2])[int.Parse(args[3], CultureInfo.InvariantCulture)], OcrB.Font, args[4]);
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        return 2;
}

static void WriteSpecimen(OcrFont ocrFont, FullPath path)
{
    var font = Font.Load(ocrFont.Build());
    string[] lines = [" !\"#$%&'()*+,-./", "0123456789:;<=>?", "@ABCDEFGHIJKLMNO", "PQRSTUVWXYZ[\\]^_", "`abcdefghijklmno", "pqrstuvwxyz{|}~", "\u00C4\u00C5\u00C6\u00D6\u00D8\u00DC\u00D1\u0132\u00E5\u00E6\u00F8\u00DF\u0133\u00A7\u00A4\u00A3", "\u00A5\u00A8\u00B4\u2440\u2441\u2442"];
    const double Size = 96;
    using var image = new Image<Rgba32>((int)(Size * 0.6 * 17), (int)(Size * 1.3 * (lines.Length + 1)), Colors.White);
    image.Draw(canvas =>
    {
        var guide = new Pen(new SolidBrush(new Rgba32(0xCC, 0xDD, 0xFF)), 1);
        for (var i = 0; i < lines.Length; i++)
        {
            var baseline = Size * 1.3 * (i + 1);
            canvas.StrokeLine(guide, new PointD(0, baseline), new PointD(image.Width, baseline));
            for (var j = 0; j < lines[i].Length; j++)
            {
                var layout = TextLayout.Create(lines[i][j].ToString(), font, Size);
                canvas.FillText(new SolidBrush(Colors.Black), layout, new PointD((Size * 0.3) + (j * Size * 0.6), baseline - layout.Baseline));
            }
        }
    });
    image.Save(path, new PngEncoder());
    Console.WriteLine(path);
}

static FullPath FindRepositoryRoot()
{
    var directory = FullPath.CurrentDirectory();
    while (!File.Exists(directory / "Meziantou.Framework.slnx"))
    {
        directory = directory.Parent;
        if (directory.IsEmpty)
            throw new InvalidOperationException("Cannot find the root of the repository.");
    }

    return directory;
}

static OcrBDigitizer.Page[] OcrBPages(string fig141, string fig142) =>
[
    new(fig141, Top: 1650, Bottom: 5000, Left: 500, Right: 3800, Rows:
            [
                "1234567890",
                "ABCDEFGHIJKLM",
                "NOPQRSTUVWXYZ",
                "abcdefghijklm",
                "nopqrstuvwxyz",
                "*+-=/.,:;\"'_",
                "?!()<>[]%#&@^",
                "\u00A4\u00A3$|\\ \u00A5",
            ]),
    new(fig142, Top: 1300, Bottom: 3100, Left: 1400, Right: 3900, Rows:
            [
                "\u00C4\u00C5\u00C6\u00D6\u00D8\u00DC\u0132\u00D1",
                "\u00E5\u00E6\u00F8\u0133\u00DF",
                "\u00A8\u00B4` ~",
                " ",
                "{}   \u00A7",
                " ",
            ],
            // The accents stand on no baseline: theirs is halfway between the rows around them, as the rows are spaced
            Baselines: [null, null, 2465, null, null, null]),
];
