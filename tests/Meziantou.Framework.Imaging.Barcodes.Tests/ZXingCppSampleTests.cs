using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes.Tests;

/// <summary>
/// Reads the sample images of zxing-cpp (<c>ZXingCppSamples</c>, see its README): photographs taken with phones and
/// scanners, with blur, perspective, uneven lighting, curved paper and noise, as well as generated images. Each image is
/// read at the rotations zxing-cpp tests it at, and the outcome of each symbol it has (read or missing) and the symbols
/// read that it does not have are compared with <c>ZXingCppSamples.Results/&lt;directory&gt;.txt</c>. A change that
/// reads more symbols updates these files: set the environment variable <c>MEZIANTOU_UPDATE_ZXINGCPP_RESULTS</c> to
/// <c>true</c> and run the tests.
/// </summary>
public sealed class ZXingCppSampleTests
{
    private const string UpdateVariable = "MEZIANTOU_UPDATE_ZXINGCPP_RESULTS";

    private static readonly Lock UpdateLock = new();

    // The symbologies of the samples that the readers do not support
    private static readonly string[] UnsupportedFormats = ["MaxiCode", "Micro PDF417", "MicroPDF417", "Telepen", "DX Film Edge", "DXFilmEdge", "Code 32", "PZN"];

    private static string SamplesDirectory => Path.Combine(AppContext.BaseDirectory, "ZXingCppSamples");

    public static TheoryData<string, string> Images()
    {
        var data = new TheoryData<string, string>();
        foreach (var directory in Directory.GetDirectories(SamplesDirectory).Order(StringComparer.Ordinal))
        {
            foreach (var file in Directory.GetFiles(directory).Where(IsImage).Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(directory), Path.GetFileName(file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void ReadSample(string directory, string file)
    {
        var lines = GetOutcomes(directory, file);
        var resultsPath = Path.Combine(AppContext.BaseDirectory, "ZXingCppSamples.Results", directory + ".txt");
        var expected = File.Exists(resultsPath) ? File.ReadAllLines(resultsPath).Where(line => line.StartsWith(file + '\t', StringComparison.Ordinal)).ToList() : [];
        if (expected.SequenceEqual(lines, StringComparer.Ordinal))
            return;

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            UpdateResults(directory, file, lines);
            return;
        }

        var message = new StringBuilder();
        message.AppendLine(CultureInfo.InvariantCulture, $"The outcomes of {directory}/{file} changed (set {UpdateVariable}=true to update ZXingCppSamples.Results/{directory}.txt when it reads more).");
        foreach (var line in expected.Except(lines, StringComparer.Ordinal))
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        foreach (var line in lines.Except(expected, StringComparer.Ordinal))
        {
            message.AppendLine(CultureInfo.InvariantCulture, $"+ {line}");
        }

        Assert.Fail(message.ToString());
    }

    [Fact]
    public void Results_ListOnlyTheSampleImages()
    {
        foreach (var resultsPath in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ZXingCppSamples.Results")))
        {
            var directory = Path.Combine(SamplesDirectory, Path.GetFileNameWithoutExtension(resultsPath));
            Assert.True(Directory.Exists(directory), $"{resultsPath} has no sample directory");
            foreach (var line in File.ReadAllLines(resultsPath))
            {
                Assert.True(File.Exists(Path.Combine(directory, line.Split('\t')[0])), $"{resultsPath}: {line}");
            }
        }
    }

    private static bool IsImage(string path) => Path.GetExtension(path) is ".png" or ".jpg" or ".webp" or ".gif";

    // One line per symbol of the image at each rotation (read or missing), then one line per symbol read that it does not have
    private static List<string> GetOutcomes(string directory, string file)
    {
        var directoryPath = Path.Combine(SamplesDirectory, directory);
        var kind = directory.Split('-')[0];
        var isLinear = kind is not ("aztec" or "datamatrix" or "pdf417" or "qrcode" or "microqrcode" or "rmqrcode" or "multi" or "none");
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal) { ["find"] = isLinear ? "sh" : "sa" };
        defaults = ReadToml(Path.Combine(directoryPath, "!defaults.toml"), defaults)[0];
        var records = ReadSymbols(Path.Combine(directoryPath, file), defaults);
        var rotations = GetRotations(records[0]["find"]);
        if (records[0].TryGetValue("search", out var search))
        {
            rotations |= GetRotations(search);
        }

        var lines = new List<string>();
        using var original = Image.Load(Path.Combine(directoryPath, file));
        for (var rotation = 0; rotation < 4; rotation++)
        {
            if ((rotations & (1 << rotation)) is 0)
                continue;

            using var image = original.CloneAs<Rgba32>();
            if (rotation > 0)
            {
                image.Rotate(rotation switch { 1 => RotateMode.Rotate90, 2 => RotateMode.Rotate180, _ => RotateMode.Rotate270 });
            }

            var found = Read(image, kind);
            var excluded = new List<Dictionary<string, string>>();
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (!record.ContainsKey("TextEscaped") && !record.ContainsKey("TextHex"))
                    continue;

                if ((record.TryGetValue("Format", out var format) && UnsupportedFormats.Any(unsupported => format.Contains(unsupported, StringComparison.OrdinalIgnoreCase))) ||
                    (GetRotations(record["find"]) & (1 << rotation)) is 0)
                {
                    excluded.Add(record);
                    continue;
                }

                var match = found.FindIndex(symbol => Matches(kind, record, symbol));
                if (match >= 0)
                {
                    found.RemoveAt(match);
                }

                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{file}\t{rotation * 90}\tsymbol {index}\t{(match >= 0 ? "read" : "missing")}"));
            }

            // A symbol that the image has but that is not expected at this rotation, or whose format the readers do not
            // support, may be read once; any other symbol read, a second copy included, is unexpected
            foreach (var record in excluded)
            {
                var match = found.FindIndex(symbol => Matches(kind, record, symbol));
                if (match >= 0)
                {
                    found.RemoveAt(match);
                }
            }

            foreach (var symbol in found)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{file}\t{rotation * 90}\tunexpected\t{symbol.Type}\t{Abbreviate(Escape(symbol.Text))}"));
            }
        }

        return lines;
    }

    private static void UpdateResults(string directory, string file, List<string> lines, [CallerFilePath] string sourceFilePath = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "ZXingCppSamples.Results", directory + ".txt");
        lock (UpdateLock)
        {
            var existing = File.Exists(path) ? File.ReadAllLines(path).Where(line => !line.StartsWith(file + '\t', StringComparison.Ordinal)) : [];
            var all = existing.Concat(lines).OrderBy(line => line.Split('\t')[0], StringComparer.Ordinal).ThenBy(line => int.Parse(line.Split('\t')[1], CultureInfo.InvariantCulture)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, all.Count is 0 ? "" : string.Join('\n', all) + '\n');
        }
    }

    private static List<Symbol> Read(Image image, string kind)
    {
        var all = kind is "multi" or "none";
        var symbols = new List<Symbol>();
        if (all || kind is "qrcode" or "microqrcode" or "rmqrcode")
        {
            symbols.AddRange(image.ReadQRCodes().Select(result => new Symbol(result.Result.Type.ToString(), result.Text, IsGs1: false)));
        }

        if (all || kind is "aztec" or "datamatrix" or "pdf417")
        {
            symbols.AddRange(image.ReadBarcodes2D().Select(result => new Symbol(result.Result.Type.ToString(), result.Text, result.Result.IsGs1)));
        }

        if (all || kind is not ("qrcode" or "microqrcode" or "rmqrcode" or "aztec" or "datamatrix" or "pdf417"))
        {
            symbols.AddRange(image.ReadBarcodes().Select(result => new Symbol(result.Result.Type.ToString(), result.Text, IsGs1: result.Text.StartsWith('(', StringComparison.Ordinal))));
        }

        return symbols;
    }

    private static bool Matches(string kind, Dictionary<string, string> record, Symbol symbol)
    {
        if (GetExpectedTypes(kind, record) is { } types && !types.Contains(symbol.Type, StringComparer.Ordinal))
            return false;

        var text = symbol.Text;
        if (record.TryGetValue("TextHex", out var hex))
        {
            var bytes = Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));
            return text == Encoding.Latin1.GetString(bytes) || text == Encoding.UTF8.GetString(bytes);
        }

        if (!record.TryGetValue("TextEscaped", out var expected))
            return false;

        // zxing-cpp reads the add-on of an EAN or UPC symbol only when the directory asks for it
        if (symbol.Type is nameof(BarcodeType.Ean13) or nameof(BarcodeType.Ean8) or nameof(BarcodeType.UpcA) or nameof(BarcodeType.UpcE) && text.Contains(' ', StringComparison.Ordinal))
        {
            var parts = text.Split(' ');
            text = record.ContainsKey("eanAddOnSymbol") ? parts[0] + parts[1] : parts[0];
        }

        // zxing-cpp gives a UPC-E symbol as its UPC-A expansion, and the UPC-A symbols as EAN-13 symbols in some directories
        if (symbol.Type is nameof(BarcodeType.UpcE) && text.Length >= 8)
        {
            text = ExpandUpcE(text[..8]) + text[8..];
        }

        var actual = Escape(text);
        if (expected == actual)
            return true;

        // A UPC-A symbol is an EAN-13 symbol whose first digit is 0: zxing-cpp and the reader may give either form
        if ((symbol.Type is nameof(BarcodeType.UpcA) or nameof(BarcodeType.UpcE) && expected == "0" + actual) ||
            (symbol.Type is nameof(BarcodeType.Ean13) && actual == "0" + expected))
        {
            return true;
        }

        // zxing-cpp decodes the Full ASCII pairs of Code 39 when they are all valid; the reader gives the characters
        if (symbol.Type is nameof(BarcodeType.Code39) && DecodeCode39FullAscii(text) is { } fullAscii && expected == Escape(fullAscii))
            return true;

        // GS1 data: zxing-cpp gives the element strings (a GS after the variable-length ones), the readers give their
        // human-readable interpretation, the application identifiers in parentheses
        if (symbol.IsGs1)
        {
            var expectedGs1 = expected.Replace("<GS>", "", StringComparison.Ordinal);
            var actualGs1 = actual.Replace("<GS>", "", StringComparison.Ordinal).Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
            return expectedGs1 == actualGs1;
        }

        return false;
    }

    // The types of the readers' results that can be the symbol: from its format, else from the sample directory; null
    // when any type can be (the images of several symbologies whose symbols give no format)
    private static string[]? GetExpectedTypes(string kind, Dictionary<string, string> record)
    {
        if (record.TryGetValue("Format", out var format))
        {
            return format switch
            {
                "Aztec" => [nameof(Barcode2DType.Aztec)],
                "Code 39" or "Code 32" or "Pharmazentralnummer" => [nameof(BarcodeType.Code39)],
                "Data Matrix" => [nameof(Barcode2DType.DataMatrix)],
                "EAN-13" => [nameof(BarcodeType.Ean13), nameof(BarcodeType.UpcA)],
                "EAN-8" => [nameof(BarcodeType.Ean8)],
                "PDF417" => [nameof(Barcode2DType.Pdf417)],
                "QR Code" => [nameof(QRCodeType.Standard)],
                "rMQR Code" => [nameof(QRCodeType.RMQR)],
                _ => throw new InvalidOperationException($"Unknown format '{format}'"),
            };
        }

        return kind switch
        {
            "aztec" => [nameof(Barcode2DType.Aztec)],
            "codabar" => [nameof(BarcodeType.Codabar)],
            "code128" => [nameof(BarcodeType.Code128), nameof(BarcodeType.Gs1128)],
            "code39" or "code39ext" => [nameof(BarcodeType.Code39)],
            "code93" => [nameof(BarcodeType.Code93)],
            "databarExp" or "databarExpStk" => [nameof(BarcodeType.Gs1DataBarExpanded), nameof(BarcodeType.Gs1DataBarExpandedStacked)],
            "databarLtd" => [nameof(BarcodeType.Gs1DataBarLimited)],
            "databarOmni" or "databarStk" => [nameof(BarcodeType.Gs1DataBar), nameof(BarcodeType.Gs1DataBarStacked), nameof(BarcodeType.Gs1DataBarStackedOmnidirectional)],
            "datamatrix" => [nameof(Barcode2DType.DataMatrix)],
            "ean13" or "upca" => [nameof(BarcodeType.Ean13), nameof(BarcodeType.UpcA)],
            "ean8" => [nameof(BarcodeType.Ean8)],
            "itf" => [nameof(BarcodeType.Itf)],
            "microqrcode" => [nameof(QRCodeType.MicroQR)],
            "pdf417" => [nameof(Barcode2DType.Pdf417)],
            "qrcode" => [nameof(QRCodeType.Standard)],
            "rmqrcode" => [nameof(QRCodeType.RMQR)],
            "upce" => [nameof(BarcodeType.UpcE)],
            _ => null,
        };
    }

    // The Full ASCII interpretation of a Code 39 text (ISO/IEC 16388, Annex F), or null when a pair is not valid
    private static string? DecodeCode39FullAscii(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('$' or '%' or '/' or '+'))
            {
                result.Append(c);
                continue;
            }

            if (i + 1 >= text.Length)
                return null;

            var next = text[++i];
            char? decoded = (c, next) switch
            {
                ('+', >= 'A' and <= 'Z') => (char)(next - 'A' + 'a'),
                ('$', >= 'A' and <= 'Z') => (char)(next - 'A' + 1),
                ('/', >= 'A' and <= 'O') => (char)(next - 'A' + '!'),
                ('/', 'Z') => ':',
                ('%', >= 'A' and <= 'E') => (char)(next - 'A' + 0x1B),
                ('%', >= 'F' and <= 'J') => (char)(next - 'F' + ';'),
                ('%', >= 'K' and <= 'O') => (char)(next - 'K' + '['),
                ('%', >= 'P' and <= 'T') => (char)(next - 'P' + '{'),
                ('%', 'U') => (char)0,
                ('%', 'V') => '@',
                ('%', 'W') => '`',
                ('%', >= 'X' and <= 'Z') => (char)0x7F,
                _ => null,
            };

            if (decoded is null)
                return null;

            result.Append(decoded.Value);
        }

        return result.ToString();
    }

    // The UPC-A number of a UPC-E number (number system, 6 digits, check digit)
    private static string ExpandUpcE(string upcE)
    {
        var digits = upcE[1..7];
        var body = digits[5] switch
        {
            '0' or '1' or '2' => digits[..2] + digits[5] + "0000" + digits[2..5],
            '3' => digits[..3] + "00000" + digits[3..5],
            '4' => digits[..4] + "00000" + digits[4],
            _ => digits[..5] + "0000" + digits[5],
        };
        return upcE[0] + body + upcE[7];
    }

    private static string Abbreviate(string value) => value.Length > 60 ? value[..60] + "..." : value;

    // The rotations of the slow mode of zxing-cpp ('s' followed by 0 to 3, 'h' for 0 and 2, 'v' for 1 and 3, 'a' for all)
    private static int GetRotations(string filter)
    {
        var result = 0;
        var slow = false;
        foreach (var c in filter)
        {
            switch (c)
            {
                case 's':
                    slow = true;
                    break;
                case 'f' or 'p':
                    slow = false;
                    break;
                case >= '0' and <= '3' when slow:
                    result |= 1 << (c - '0');
                    break;
                case 'h' when slow:
                    result |= 0b0101;
                    break;
                case 'v' when slow:
                    result |= 0b1010;
                    break;
                case 'a' when slow:
                    result |= 0b1111;
                    break;
            }
        }

        return result;
    }

    // The symbols of an image: its .toml or .txt file, found by removing the suffixes of the name ("-2", "_b", "!")
    private static List<Dictionary<string, string>> ReadSymbols(string imagePath, Dictionary<string, string> defaults)
    {
        var directory = Path.GetDirectoryName(imagePath)!;
        var stem = Path.GetFileNameWithoutExtension(imagePath);
        string? dataPath = null;
        for (var name = stem; dataPath is null;)
        {
            if (File.Exists(Path.Combine(directory, name + ".toml")))
            {
                dataPath = Path.Combine(directory, name + ".toml");
            }
            else if (File.Exists(Path.Combine(directory, name + ".txt")))
            {
                dataPath = Path.Combine(directory, name + ".txt");
            }
            else
            {
                var separator = name.LastIndexOfAny(['-', '_', '!']);
                if (separator < 0)
                    break;

                name = name[..separator];
            }
        }

        List<Dictionary<string, string>> records;
        if (dataPath is not null && dataPath.EndsWith(".toml", StringComparison.Ordinal))
        {
            records = ReadToml(dataPath, defaults);
        }
        else
        {
            records = [new(defaults, StringComparer.Ordinal)];
            if (dataPath is not null)
            {
                records[0]["TextEscaped"] = Escape(File.ReadAllText(dataPath, Encoding.UTF8));
            }
        }

        foreach (var record in records)
        {
            if (record.TryGetValue("TextPlain", out var plain))
            {
                record["TextEscaped"] = Escape(plain);
            }
        }

        return records;
    }

    // The subset of TOML of the samples: key = value pairs, the top-level ones being the defaults of the [[symbol]] tables
    private static List<Dictionary<string, string>> ReadToml(string path, Dictionary<string, string> defaults)
    {
        if (!File.Exists(path))
            return [new(defaults, StringComparer.Ordinal)];

        var records = new List<Dictionary<string, string>>();
        var current = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        var atTopLevel = true;
        foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length is 0 || line[0] is '#')
                continue;

            if (line is "[[symbol]]")
            {
                if (atTopLevel)
                {
                    atTopLevel = false;
                    defaults = current;
                }
                else
                {
                    records.Add(current);
                }

                current = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '"' && value[^1] is '"')
            {
                value = Unescape(value[1..^1]);
            }

            current[key] = value;
        }

        records.Add(current);
        return records;
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not '\\')
            {
                result.Append(value[i]);
                continue;
            }

            var next = value[++i];
            switch (next)
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case 'u' or 'U':
                    var length = next is 'u' ? 4 : 8;
                    result.Append(char.ConvertFromUtf32(int.Parse(value.AsSpan(i + 1, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    i += length;
                    break;
                default:
                    result.Append(next);
                    break;
            }
        }

        return result.ToString();
    }

    // The escaping of zxing-cpp (EscapeNonGraphical): the control characters by their names, the non-breaking spaces by their code points
    private static string Escape(string value)
    {
        string[] names = ["NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "BEL", "BS", "HT", "LF", "VT", "FF", "CR", "SO", "SI", "DLE", "DC1", "DC2", "DC3", "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "FS", "GS", "RS", "US", "DEL"];
        var result = new StringBuilder();
        foreach (var c in value)
        {
            if (c < 32 || c is '\u007F')
            {
                result.Append('<').Append(names[c is '\u007F' ? 32 : c]).Append('>');
            }
            else if (c is '\u00A0' or '\u2000' or '\u2007' or '\uFFFD' or (>= '\u0080' and < '\u00A0'))
            {
                result.Append("<U+").Append(((int)c).ToString(c < 256 ? "X2" : "X4", CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private sealed record Symbol(string Type, string Text, bool IsGs1);
}
