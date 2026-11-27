using System.Text;
using System.Xml.Linq;
using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// The render options of the symbols: <see cref="QRCodeRenderer.CreateScene"/> and the others reject an option out of range
/// with an <see cref="ArgumentOutOfRangeException"/>, and logo data that is not an image with an <see cref="ArgumentException"/>,
/// exactly when the option is invalid; the scene has the size of the modules and the quiet zone, which Imaging measures, the
/// SVG is XML and the image has the size of the scene, rounded up. The console renderers reject invalid options the same
/// way, and give a line per two pixel rows of the modules, followed by the centered text of a linear barcode. The text of
/// EAN and UPC symbols is in groups, inside the scene, overlapping neither the bars nor the other groups. A QR code logo
/// clears exactly the modules of the rectangle of whole modules around it, and only when the code stays readable with
/// every codeword of the area wrong.
/// </summary>
internal static class RenderFuzzTargets
{
    private const int KindCount = 17;

    private static readonly double[] ModuleSizes = [1, 2, 3, 0.5, 1.5, 2.6, 4, 7.3, 0.1, 0, -1, double.NaN, double.PositiveInfinity, 1e7, double.NegativeInfinity, 1e-300];

    private static readonly double[] FontSizes = [14, 8, 30, 0.5, 200, 0, -1, double.NaN, 1e6, double.PositiveInfinity, 1e-9, 3, 14, 20, 6, 10];

    private static readonly double[] Spacings = [2, 0, 5.5, 0.4, -1, double.NaN, double.PositiveInfinity, 1e6, 10, 2, 1, 0.5, 3, 7, -0.1, 4];

    private static readonly int[] LogoSizePercents = [20, 1, 50, 100, 0, 101, -5, 10, 99, 30, int.MaxValue, int.MinValue];

    private static readonly int[] LogoMargins = [0, 1, 2, -1];

    private static readonly double[] BackdropCornerRadii = [0, 1.5, -1, double.NaN];

    private static readonly SceneColor BackdropColor = new(0xee, 0xee, 0xee);

    // A PNG image of 1x1 green pixel, the logo of the unit tests
    private static readonly byte[] GreenPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mNg+M8AAAICAQBF9FLUAAAAAElFTkSuQmCC");

    // The data of each kind of symbol in the unit tests: QR codes, the linear barcodes, a stacked one, then 2D symbols,
    // UPC-E, and a QR code of level H. The data of EAN and UPC is followed by "+" and the add-on, if any.
    private static readonly string[] SeedTexts =
    [
        "HELLO WORLD", "12345", "HELLO",
        "ABC-123", "Hello 12345678", "Hello 12345678", "5512345", "590123412345", "03600029145", "40156", "12345678",
        "0950110153001",
        "Hello", "Hello", "Hello",
        "0425261",
        "https://www.meziantou.net",
    ];

    // The EAN and UPC data of the unit tests, with an add-on
    private static readonly (int Kind, string Text)[] AddOnSeedTexts = [(6, "5512345+12"), (7, "978020137962+52499"), (8, "03600029145+12"), (15, "0425261+52495")];

    // The signatures of the image formats a logo can be, and the elements of an SVG document
    private static readonly string[] Tokens =
    [
        "\u0089PNG\r\n\u001A\n", "IHDR", "IDAT", "IEND", "ÿØÿ", "GIF89a", "GIF87a", "RIFF", "WEBP", "VP8 ", "VP8L",
        "HELLO WORLD", "12345", "ABC-123", "590123412345", "0950110153001", "0425261", "+12", "+52499", "<", ">", "&", "\"", "'", "]]>", "\u0001", "\n", "\u001b[", "\u0085", "￾", "😀",
    ];

    // The signatures and the chunks, segments and markers the size reader looks at
    private static readonly string[] LogoSizeTokens =
    [
        "\u0089PNG\r\n\u001A\n", "IHDR", "IDAT", "eXIf", "IEND", "GIF89a", "GIF87a", "RIFF", "WEBP", "VP8 ", "VP8L", "VP8X",
        "\u009d\u0001*", "/", "ÿØ", "ÿá", "Exif\u0000", "ÿÀ", "ÿÂ", "ÿÚ", "ÿÙ", "ÿÿ",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("barcodes-scene", SceneTarget) { Seeds = GetSceneSeeds, Dictionary = Tokens },
        new("barcodes-console", ConsoleTarget) { Seeds = GetConsoleSeeds, Dictionary = Tokens },
        new("barcodes-logo-size", LogoSizeTarget) { Seeds = GetLogoSizeSeeds, Dictionary = LogoSizeTokens },
    ];


    private static void SceneTarget(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data) % KindCount;
        var moduleSize = FuzzInput.Pick(ModuleSizes, FuzzInput.ReadByte(ref data));
        var moduleHeight = FuzzInput.Pick(ModuleSizes, FuzzInput.ReadByte(ref data));
        var quietZone = (FuzzInput.ReadByte(ref data) % 14) - 2;
        var flags = FuzzInput.ReadByte(ref data);
        var textByte = FuzzInput.ReadByte(ref data);
        var logoSizePercent = FuzzInput.Pick(LogoSizePercents, FuzzInput.ReadByte(ref data));
        var logoFlags = FuzzInput.ReadByte(ref data);
        var text = FuzzInput.ReadText(FuzzInput.ReadUntil(ref data, 0));
        var logoData = data.ToArray();

        var snap = (flags & 1) == 0;
        var noLight = (flags & 2) != 0;
        var gradient = (flags & 4) != 0;
        var withText = (flags & 8) != 0;
        var withLogo = (flags & 16) != 0;
        var fonts = (flags & 32) == 0 ? BarcodeFonts.OcrB : BarcodeFonts.OcrA;
        var fontSize = FontSizes[textByte & 15];
        var spacing = Spacings[textByte >> 4];
        var clearModules = (logoFlags & 1) == 0;
        var logoMargin = LogoMargins[(logoFlags >> 1) & 3];
        var withBackdrop = (logoFlags & 8) != 0;
        var backdropCornerRadius = BackdropCornerRadii[(logoFlags >> 4) & 3];

        var symbol = CreateSymbol(kind, text);
        if (symbol is null)
            return;

        ScenePaint dark = gradient ? new LinearGradientPaint(0, 0, 100, 0, [new SceneGradientStop(0, SceneColor.Black), new SceneGradientStop(1, new SceneColor(0, 0, 255))]) : SceneColor.Black;
        ScenePaint? light = noLight ? null : (ScenePaint)SceneColor.White;

        // The options each kind of symbol has, and whether they are valid
        var hasText = false;
        var heightIsValidated = false;
        var textIsInvalid = false;
        var logoSizeIsInvalid = false;
        var logoDataIsInvalid = false;
        var logoMayBeUnreadable = false;
        var hasLogo = false;
        Func<Scene> createScene;
        Func<string> toSvg;
        Func<RectangleD> measure;
        Func<Image<Rgba32>> toImage;
        switch (symbol)
        {
            case QRCode qr:
                var qrOptions = new QRCodeRenderOptions { ModuleSize = moduleSize, QuietZoneModules = quietZone, DarkPaint = dark, LightPaint = light, SnapToPixels = snap };
                if (withLogo)
                {
                    qrOptions.Logo = new BarcodeLogo(logoData)
                    {
                        SizePercent = logoSizePercent,
                        ClearModules = clearModules,
                        MarginModules = logoMargin,
                        BackdropPaint = withBackdrop ? (ScenePaint)BackdropColor : null,
                        BackdropCornerRadius = backdropCornerRadius,
                    };
                    logoSizeIsInvalid = logoSizePercent is < 1 or > 100 || logoMargin < 0 || !double.IsFinite(backdropCornerRadius) || backdropCornerRadius < 0;
                    logoMayBeUnreadable = clearModules;
                    hasLogo = true;
                    logoDataIsInvalid = SceneImage.GetMediaType(logoData) is null;
                }

                createScene = () => qr.CreateScene(qrOptions);
                toSvg = () => qr.ToSvg(qrOptions);
                measure = () => QRCodeImageRenderer.MeasureQRCode(qr, qrOptions);
                toImage = () => qr.ToImage(qrOptions);
                break;

            case Barcode barcode:
                var barcodeOptions = new BarcodeRenderOptions { ModuleWidth = moduleSize, ModuleHeight = moduleHeight, QuietZoneModules = quietZone, DarkPaint = dark, LightPaint = light, SnapToPixels = snap };
                if (withText)
                {
                    barcodeOptions.Text = new BarcodeTextOptions { Fonts = fonts, FontSize = fontSize, Spacing = spacing, Paint = (flags & 64) != 0 ? (ScenePaint)new SceneColor(255, 0, 0) : null };
                    textIsInvalid = !IsPositiveFinite(fontSize) || !double.IsFinite(spacing) || spacing < 0;
                    hasText = barcode.Text.Length > 0;
                }

                heightIsValidated = !barcode.IsStacked;
                createScene = () => barcode.CreateScene(barcodeOptions);
                toSvg = () => barcode.ToSvg(barcodeOptions);
                measure = () => BarcodeImageRenderer.MeasureBarcode(barcode, barcodeOptions);
                toImage = () => barcode.ToImage(barcodeOptions);
                break;

            default:
                var barcode2D = (Barcode2D)symbol;
                var barcode2DOptions = new Barcode2DRenderOptions { ModuleSize = moduleSize, QuietZoneModules = quietZone, DarkPaint = dark, LightPaint = light, SnapToPixels = snap };
                createScene = () => barcode2D.CreateScene(barcode2DOptions);
                toSvg = () => barcode2D.ToSvg(barcode2DOptions);
                measure = () => Barcode2DImageRenderer.MeasureBarcode2D(barcode2D, barcode2DOptions);
                toImage = () => barcode2D.ToImage(barcode2DOptions);
                break;
        }

        var (columns, rows, isDark) = GetModules(symbol);
        var isLinear = symbol is Barcode;
        var isStacked = symbol is Barcode { IsStacked: true };

        // The module sizes and the quiet zone are checked first, then the size of the logo, and its data when the scene adds it
        var outOfRange = quietZone < 0 || !IsPositiveFinite(moduleSize) || (heightIsValidated && !IsPositiveFinite(moduleHeight)) || textIsInvalid || logoSizeIsInvalid;
        Scene scene;
        try
        {
            scene = createScene();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A cleared area the QR code cannot lose is rejected after the options are validated
            var isUnreadableLogo = ex.Message.StartsWith("The area cleared under the logo", StringComparison.Ordinal);
            FuzzAssert.True(isUnreadableLogo ? !outOfRange && logoMayBeUnreadable : outOfRange, $"CreateScene rejects valid options: {ex.Message}");
            AssertThrows<ArgumentOutOfRangeException>(() => toSvg(), "ToSvg");
            AssertThrows<ArgumentOutOfRangeException>(() => measure(), "Measure");
            AssertThrows<ArgumentOutOfRangeException>(() => toImage().Dispose(), "ToImage");
            return;
        }
        catch (ArgumentException ex)
        {
            FuzzAssert.True(!outOfRange && logoDataIsInvalid, $"CreateScene rejects valid logo data: {ex.Message}");
            return;
        }

        FuzzAssert.False(outOfRange, "CreateScene accepts an option out of range");
        FuzzAssert.False(logoDataIsInvalid, "CreateScene accepts a logo that is not an image");

        // The size of the modules and the quiet zone, horizontal only for a linear barcode; the text can widen the drawing
        var moduleWidth = snap ? Math.Max(1, Math.Round(moduleSize, MidpointRounding.AwayFromZero)) : moduleSize;
        var effectiveModuleHeight = isLinear && !isStacked ? (snap ? Math.Max(1, Math.Round(moduleHeight, MidpointRounding.AwayFromZero)) : moduleHeight) : moduleWidth;
        var verticalQuietZone = isLinear ? 0 : quietZone;
        var symbolWidth = (columns + (2.0 * quietZone)) * moduleWidth;
        var symbolHeight = (rows + (2.0 * verticalQuietZone)) * effectiveModuleHeight;
        if (hasText)
        {
            // The scene adds up the quiet zones and the bars: the sum can differ from the product by a rounding error
            FuzzAssert.True(scene.Width >= symbolWidth * (1 - 1e-12) && scene.Height > symbolHeight,$"The scene ({scene.Width}x{scene.Height}) is smaller than the symbol and its text ({symbolWidth}x{symbolHeight})");

            // The text is one line, as it is measured, except the groups of digits of EAN and UPC: its control characters are
            // drawn as '?'
            var sceneTexts = scene.Root.Children.OfType<SceneText>().ToArray();
            FuzzAssert.True(sceneTexts.Length == 1 || symbol is Barcode { EanUpcLayout: not null }, $"The text of the scene is {sceneTexts.Length} lines");
            FuzzAssert.False(sceneTexts.Any(sceneText => sceneText.Text.Any(char.IsControl)), "The text of the scene has a control character");
        }
        else
        {
            FuzzAssert.Equal((symbolWidth, symbolHeight), (scene.Width, scene.Height), "The size of the scene");
        }

        FuzzAssert.Equal(new RectangleD(0, 0, scene.Width, scene.Height), measure(), "The measured rectangle is the size of the scene");
        if (hasText && symbol is Barcode { EanUpcLayout: not null } eanUpc)
        {
            AssertEanUpcLayout(eanUpc, scene, fonts);
        }

        // The cleared area is the rectangle of whole modules around the logo, grown by the margin
        ModuleRectangle? cleared = null;
        if (symbol is QRCode clearedQRCode && hasLogo)
        {
            var logo = scene.Root.Children.OfType<SceneImage>().Single();
            if (clearModules)
            {
                cleared = GetClearedArea(GetLogoBounds(logo), quietZone, moduleWidth, logoMargin, columns, rows);
                if (cleared is { } area)
                {
                    AssertLosesTheArea(clearedQRCode, area);
                }
            }

            var backdrop = scene.Root.Children.OfType<SceneRectangle>().Where(rectangle => rectangle.Fill is SolidPaint { Color: var color } && color == BackdropColor).ToList();
            if (withBackdrop && cleared is { } backdropArea)
            {
                FuzzAssert.Equal(1, backdrop.Count, "The number of backdrops");
                var rectangle = backdrop[0];
                // Placed as the modules are: after the quiet zone, then the modules before it (fuzz regression: the
                // product of their sum can differ by a rounding error)
                var expected = ((quietZone * moduleWidth) + (backdropArea.Column * moduleWidth), (quietZone * moduleWidth) + (backdropArea.Row * moduleWidth), backdropArea.ColumnCount * moduleWidth, backdropArea.RowCount * moduleWidth);
                FuzzAssert.Equal(expected, (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), "The backdrop is on the module boundaries of the cleared area");
                FuzzAssert.True(rectangle.CornerRadius >= 0 && rectangle.CornerRadius <= Math.Min(rectangle.Width, rectangle.Height) / 2, $"The corner radius of the backdrop is {rectangle.CornerRadius}");
            }
            else
            {
                FuzzAssert.Equal(0, backdrop.Count, "No backdrop without a cleared area");
            }
        }

        var svg = toSvg();
        XDocument document;
        try
        {
            document = XDocument.Parse(svg);
        }
        catch (System.Xml.XmlException ex)
        {
            FuzzAssert.Fail($"ToSvg is not XML: {ex.Message}");
            return;
        }

        FuzzAssert.Equal("svg", document.Root?.Name.LocalName, "The root element of the SVG");

        if (scene.Width > 1024 || scene.Height > 1024)
            return;

        using var image = toImage();
        FuzzAssert.Equal(((int)Math.Ceiling(scene.Width), (int)Math.Ceiling(scene.Height)), (image.Width, image.Height), "The size of the image is the size of the scene rounded up");

        // With whole sizes and solid paints, the center of each module is its color: the light paint, or the backdrop, in the
        // cleared area. The logo covers the modules under it.
        if (!snap || gradient || noLight || hasText || (hasLogo && !clearModules))
            return;

        var logoBounds = hasLogo ? GetLogoBounds(scene.Root.Children.OfType<SceneImage>().Single()) : default;
        var pixels = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = (int)((quietZone + column + 0.5) * moduleWidth);
                var y = (int)((verticalQuietZone + row + 0.5) * effectiveModuleHeight);
                if (hasLogo && x + 1 >= logoBounds.X && x <= logoBounds.X + logoBounds.Width && y + 1 >= logoBounds.Y && y <= logoBounds.Y + logoBounds.Height)
                    continue;

                var isCleared = cleared is { } area && area.Contains(row, column);
                if (isCleared && withBackdrop)
                {
                    // The backdrop, or the light paint and the edge of the backdrop in its rounded corners
                    var pixel = pixels[(y * image.Width) + x];
                    if (pixel.R < BackdropColor.R || pixel.G < BackdropColor.G || pixel.B < BackdropColor.B)
                        FuzzAssert.Fail($"Cleared module ({column}, {row}) at pixel ({x}, {y}) is {pixel}, expected the backdrop");

                    continue;
                }

                var expected = isDark(row, column) && !isCleared ? Colors.Black : Colors.White;
                if (pixels[(y * image.Width) + x] != expected)
                    FuzzAssert.Fail($"Module ({column}, {row}) at pixel ({x}, {y}) is {pixels[(y * image.Width) + x]}, expected {expected}");
            }
        }
    }

    // Small logos: a header that declares a large image fails at the limits instead of allocating hundreds of megabytes
    private static readonly ImageDecodeOptions LogoDecodeOptions = new()
    {
        Configuration = new ImageConfiguration
        {
            Limits = new ImageResourceLimits { MaxEncodedBytes = 4 * 1024 * 1024, MaxFramePixels = 1 << 22, MaxLiveAllocationBytes = 256 * 1024 * 1024 },
        },
    };

    /// <summary>
    /// The size a logo covers in its square comes from the header of the image: when the reader gives one, the decoder of
    /// Imaging, which the image renderer uses, decodes the image at that size, or rejects the data.
    /// </summary>
    private static void LogoSizeTarget(ReadOnlySpan<byte> data)
    {
        if (!LogoImageSize.TryGetSize(data, out var width, out var height))
            return;

        FuzzAssert.True(width > 0 && height > 0, $"The size of the logo is {width}x{height}");
        Image image;
        try
        {
            image = Image.Load(data, LogoDecodeOptions);
        }
        catch (ImageException)
        {
            // Also ImageResourceLimitException: a header that declares a large image
            return;
        }

        using (image)
        {
            FuzzAssert.Equal((image.Frames[0].Width, image.Frames[0].Height), (width, height), "The size of the decoded logo");
        }
    }

    private static IEnumerable<FuzzSeed> GetLogoSizeSeeds()
    {
        yield return new FuzzSeed("logo-size-green-png", GreenPng);
        (string Name, ImageEncoder Encoder)[] encoders =
        [
            ("png", new PngEncoder()),
            ("gif", new GifEncoder()),
            ("jpeg", new JpegEncoder()),
            ("jpeg-progressive", new JpegEncoder { Process = JpegEncodingProcess.Progressive }),
            ("webp-lossless", new WebPEncoder()),
            ("webp-lossy", new WebPEncoder { Compression = WebPCompression.Lossy }),
        ];

        foreach (var (name, encoder) in encoders)
        {
            foreach (var (width, height) in new[] { (37, 21), (1, 1), (16, 64) })
            {
                using var image = new Image<Rgba32>(width, height, new Rgba32(0x25, 0x63, 0xeb));
                using var stream = new MemoryStream();
                image.Save(stream, encoder);
                yield return new FuzzSeed($"logo-size-{name}-{width}x{height}", stream.ToArray());
            }
        }

        // A JPEG with Exif metadata, whose orientation can turn the image (the unit tests)
        yield return new FuzzSeed("logo-size-jpeg-exif", (byte[])[0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x08, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x02, 0x01, 0x01, 0x11, 0x00]);
    }

    private static void ConsoleTarget(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data) % KindCount;
        var flags = FuzzInput.ReadByte(ref data);
        var quietZoneByte = FuzzInput.ReadByte(ref data);
        var moduleWidth = FuzzInput.ReadByte(ref data) % 4;
        var moduleHeight = FuzzInput.ReadByte(ref data) % 4;
        var barHeight = FuzzInput.ReadByte(ref data) % 6;
        var text = FuzzInput.ReadText(data);

        var symbol = CreateSymbol(kind, text);
        if (symbol is null)
            return;

        var options = new ConsoleRenderOptions
        {
            InvertColors = (flags & 1) != 0,
            ShowText = (flags & 2) != 0,
            QuietZoneModules = (flags & 4) != 0 ? null : (quietZoneByte % 8) - 1,
            ModuleWidth = moduleWidth,
            ModuleHeight = moduleHeight,
            BarHeight = barHeight,
        };

        Func<string> render = symbol switch
        {
            QRCode qr => () => qr.ToConsoleString(options),
            Barcode barcode => () => barcode.ToConsoleString(options),
            _ => () => ((Barcode2D)symbol).ToConsoleString(options),
        };

        var invalid = moduleWidth < 1 || moduleHeight < 1 || barHeight < 1 || options.QuietZoneModules < 0;
        string console;
        try
        {
            console = render();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            FuzzAssert.True(invalid, $"ToConsoleString rejects valid options: {ex.Message}");
            return;
        }

        FuzzAssert.False(invalid, "ToConsoleString accepts an option out of range");
        FuzzAssert.False(console.Replace(Environment.NewLine, "", StringComparison.Ordinal).Any(char.IsControl), "The output has a control character other than its new lines");

        // The quiet zone of a linear barcode is horizontal only, and each of its rows is BarHeight characters high
        var (columns, rows, isDark) = GetModules(symbol);
        var linear = symbol as Barcode;
        var quietZone = options.QuietZoneModules ?? (linear is null ? 2 : 10);
        var verticalQuietZone = linear is null ? quietZone : 0;
        var pixelHeight = linear is { IsStacked: false } ? barHeight * 2 : moduleHeight;
        var totalWidth = columns + (2 * quietZone);
        var totalHeight = (rows + (2 * verticalQuietZone)) * pixelHeight;
        var lineLength = totalWidth * moduleWidth;

        // The text of a linear barcode is the last line, centered
        var symbolPart = console;
        if (linear is not null && options.ShowText)
        {
            // Control characters are written as '?': the text cannot break the layout or send escape sequences to the terminal
            var printable = string.Concat(linear.Text.Select(c => char.IsControl(c) ? '?' : c));
            var textLine = (new string(' ', Math.Max(0, (lineLength - printable.Length) / 2)) + printable).TrimEnd(' ');
            FuzzAssert.True(console.EndsWith(Environment.NewLine + textLine, StringComparison.Ordinal), "The last line is the centered text of the barcode");
            symbolPart = console[..^(Environment.NewLine.Length + textLine.Length)];
        }

        var lines = symbolPart.Split(Environment.NewLine);
        FuzzAssert.Equal((totalHeight + 1) / 2, lines.Length, "The number of lines of the symbol");
        var expected = new StringBuilder();
        for (var line = 0; line < lines.Length; line++)
        {
            FuzzAssert.True(lines[line].Length <= lineLength, $"Line {line} is longer than {lineLength} characters");

            expected.Clear();
            for (var column = 0; column < totalWidth; column++)
            {
                var top = IsDark(2 * line);
                var bottom = (2 * line) + 1 < totalHeight && IsDark((2 * line) + 1);
                expected.Append((top, bottom) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' ',
                }, moduleWidth);

                bool IsDark(int pixelRow)
                {
                    var row = (pixelRow / pixelHeight) - verticalQuietZone;
                    var moduleColumn = column - quietZone;
                    var dark = row >= 0 && row < rows && moduleColumn >= 0 && moduleColumn < columns && isDark(row, moduleColumn);
                    return dark != options.InvertColors;
                }
            }

            FuzzAssert.Equal(expected.ToString().TrimEnd(' '), lines[line], $"Line {line} of the console string");
        }
    }

    // The pixels of the logo keep their proportions, centered in its square; the whole square when their size is unknown,
    // or when Exif metadata can turn them (barcodes-logo-size checks the size against the decoder)
    private static RectangleD GetLogoBounds(SceneImage logo)
    {
        if (!LogoImageSize.TryGetSize(logo.Data.Span, out var pixelWidth, out var pixelHeight))
            return new RectangleD(logo.X, logo.Y, logo.Width, logo.Height);

        var scale = Math.Min(logo.Width / pixelWidth, logo.Height / pixelHeight);
        var width = pixelWidth * scale;
        var height = pixelHeight * scale;
        return new RectangleD(logo.X + ((logo.Width - width) / 2), logo.Y + ((logo.Height - height) / 2), width, height);
    }

    private static ModuleRectangle? GetClearedArea(RectangleD logo, int quietZone, double moduleSize, int margin, int columns, int rows)
    {
        var first = (int)Math.Clamp(Math.Floor((logo.X / moduleSize) - quietZone + 1e-9) - margin, 0, columns);
        var end = (int)Math.Clamp(Math.Ceiling(((logo.X + logo.Width) / moduleSize) - quietZone - 1e-9) + margin, 0, columns);
        var firstRow = (int)Math.Clamp(Math.Floor((logo.Y / moduleSize) - quietZone + 1e-9) - margin, 0, rows);
        var endRow = (int)Math.Clamp(Math.Ceiling(((logo.Y + logo.Height) / moduleSize) - quietZone - 1e-9) + margin, 0, rows);
        return end > first && endRow > firstRow ? new ModuleRectangle(firstRow, first, endRow - firstRow, end - first) : null;
    }

    // The worst case of a logo: every module of the area is wrong, so every codeword with a module in it is wrong too. The
    // error correction still recovers the data.
    private static void AssertLosesTheArea(QRCode qrCode, ModuleRectangle area)
    {
        var modules = new bool[qrCode.Height, qrCode.Width];
        for (var row = 0; row < qrCode.Height; row++)
        {
            for (var column = 0; column < qrCode.Width; column++)
            {
                modules[row, column] = qrCode[row, column] != area.Contains(row, column);
            }
        }

        var expected = QRCodeDecoder.Decode(qrCode);
        FuzzAssert.True(QRCodeDecoder.TryDecode(modules, out var actual), $"The QR code is not readable without the modules cleared under the logo ({area})");
        FuzzAssert.Equal(expected.Text, actual.Text, "The text of the QR code without the modules cleared under the logo");
    }

    private static object? CreateSymbol(int kind, string text)
    {
        try
        {
            return kind switch
            {
                0 => QRCode.Create(text, ErrorCorrectionLevel.L),
                1 => QRCode.CreateMicroQR(text, ErrorCorrectionLevel.L),
                2 => QRCode.CreateRMQR(text, ErrorCorrectionLevel.M),
                3 => Barcode.CreateCode39(text),
                4 => Barcode.CreateCode93(text),
                5 => Barcode.CreateCode128(text),
                6 => Barcode.CreateEan8(GetData(text), GetAddOn(text)),
                7 => Barcode.CreateEan13(GetData(text), GetAddOn(text)),
                8 => Barcode.CreateUpcA(GetData(text), GetAddOn(text)),
                9 => Barcode.CreateCodabar(text),
                10 => Barcode.CreateItf(text),
                11 => Barcode.CreateGs1DataBarStacked(text),
                12 => Barcode2D.CreateDataMatrix(text),
                13 => Barcode2D.CreatePdf417(text),
                14 => Barcode2D.CreateAztec(text),
                15 => Barcode.CreateUpcE(GetData(text), GetAddOn(text)),
                _ => QRCode.Create(text, ErrorCorrectionLevel.H),
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // The data does not fit
            return null;
        }
    }

    // The data of EAN and UPC, and the add-on after a "+"
    private static string GetData(string text) => text.IndexOf('+', StringComparison.Ordinal) is var index and >= 0 ? text[..index] : text;

    private static string? GetAddOn(string text) => text.IndexOf('+', StringComparison.Ordinal) is var index and >= 0 ? text[(index + 1)..] : null;

    // The groups of digits are the text without its space, inside the scene, and overlap neither the bars nor each other; the
    // start guard reaches the bottom of the bars
    private static void AssertEanUpcLayout(Barcode barcode, Scene scene, SceneFontCollection fonts)
    {
        var tolerance = 1e-9 * Math.Max(1, Math.Max(scene.Width, scene.Height));
        var measurer = new FontFaceTextMeasurer(fonts);
        var texts = scene.Root.Children.OfType<SceneText>().ToArray();
        FuzzAssert.Equal(barcode.Text.Replace(" ", "", StringComparison.Ordinal), string.Concat(texts.Select(text => text.Text)), "The groups of digits are the text");

        var boxes = texts.Select(text => new RectangleD(text.X, text.Y, text.MeasuredWidth, measurer.Measure(text.Text, text.Font).Height)).ToArray();
        var commands = scene.Root.Children.OfType<ScenePath>().Single().Data.Commands;
        var bars = new RectangleD[commands.Count / 5];
        for (var i = 0; i < bars.Length; i++)
        {
            var (left, top) = (commands[i * 5].X, commands[i * 5].Y);
            bars[i] = new RectangleD(left, top, commands[(i * 5) + 2].X - left, commands[(i * 5) + 2].Y - top);
        }

        FuzzAssert.Equal(bars.Max(bar => bar.Bottom), bars[0].Bottom, "The start guard extends below the data bars");
        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            FuzzAssert.True(box.X >= -tolerance && box.Y >= -tolerance && box.Right <= scene.Width + tolerance && box.Bottom <= scene.Height + tolerance, $"The text '{texts[i].Text}' {box} is outside the scene ({scene.Width}x{scene.Height})");
            foreach (var bar in bars)
            {
                FuzzAssert.False(Overlap(box, bar), $"The text '{texts[i].Text}' {box} overlaps the bar {bar}");
            }

            for (var j = i + 1; j < boxes.Length; j++)
            {
                FuzzAssert.False(Overlap(box, boxes[j]), $"The texts '{texts[i].Text}' and '{texts[j].Text}' overlap");
            }
        }

        bool Overlap(RectangleD a, RectangleD b) => a.X < b.Right - tolerance && b.X < a.Right - tolerance && a.Y < b.Bottom - tolerance && b.Y < a.Bottom - tolerance;
    }

    private static (int Columns, int Rows, Func<int, int, bool> IsDark) GetModules(object symbol) => symbol switch
    {
        QRCode qr => (qr.Width, qr.Height, (row, column) => qr[row, column]),
        Barcode barcode => (barcode.Width, barcode.Height, (row, column) => barcode[row, column]),
        _ => (((Barcode2D)symbol).Width, ((Barcode2D)symbol).Height, (row, column) => ((Barcode2D)symbol)[row, column]),
    };

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static void AssertThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        FuzzAssert.Fail($"{name} accepts the options CreateScene rejects");
    }

    private static IEnumerable<FuzzSeed> GetSceneSeeds()
    {
        // The options of the unit tests (default sizes, no quiet zone, fractional sizes, text, logo), and invalid ones
        // The logo flags: 1 draws the modules under the logo, then the margin (bits 1-2), 8 adds a backdrop, and the corner
        // radius of the backdrop (bits 4-5)
        (string Name, byte ModuleSize, byte ModuleHeight, byte QuietZone, byte Flags, byte TextByte, byte LogoSizePercent, byte LogoFlags, bool Logo)[] variants =
        [
            ("default", 1, 1, 6, 0, 0, 0, 0, false),
            ("no-quiet-zone", 0, 0, 2, 0, 0, 0, 0, false),
            ("fractional", 4, 5, 3, 1, 0, 0, 0, false),
            ("no-light-gradient", 6, 6, 4, 6, 0, 0, 0, false),
            ("text", 1, 6, 12, 8, 0x21, 0, 0, true),
            ("text-ocra-red", 2, 6, 4, 8 | 32 | 64, 0x12, 0, 0, false),
            ("logo", 1, 1, 6, 16, 0, 0, 0, true),
            ("logo-not-cleared", 1, 1, 6, 16, 0, 0, 1, true),
            ("logo-small", 1, 1, 6, 16, 0, 7, 0, true),
            ("logo-margin", 1, 1, 6, 16, 0, 7, 2, true),
            ("logo-backdrop", 1, 1, 6, 16, 0, 0, 8, true),
            ("logo-backdrop-rounded", 3, 3, 6, 16, 0, 0, 8 | 2 | 16, true),
            ("logo-fractional-backdrop", 4, 4, 3, 17, 0, 7, 8, true),
            ("logo-no-light-backdrop", 1, 1, 6, 16 | 2, 0, 0, 8, true),
            ("logo-full", 3, 3, 2, 17, 0, 3, 0, true),
            ("logo-full-not-cleared", 3, 3, 2, 17, 0, 3, 1, true),
            ("logo-invalid-data", 1, 1, 6, 16, 0, 0, 0, false),
            ("logo-invalid-size", 1, 1, 6, 16, 0, 4, 0, true),
            ("logo-invalid-margin", 1, 1, 6, 16, 0, 0, 6, true),
            ("logo-invalid-radius", 1, 1, 6, 16, 0, 0, 8 | 32, true),
            ("logo-nan-radius", 1, 1, 6, 16, 0, 0, 8 | 48, true),
            ("nan", 11, 11, 6, 0, 0, 0, 0, false),
            ("negative-quiet-zone", 1, 1, 0, 0, 0, 0, 0, false),
            ("invalid-text", 1, 1, 6, 8, 0x45, 0, 0, false),
            ("huge", 13, 13, 6, 0, 0, 0, 0, false),
        ];

        for (var kind = 0; kind < KindCount; kind++)
        {
            foreach (var variant in variants)
            {
                byte[] logo = variant.Logo ? [0, .. GreenPng] : [0, 1, 2, 3];
                yield return new FuzzSeed($"scene-{kind}-{variant.Name}", (byte[])[(byte)kind, variant.ModuleSize, variant.ModuleHeight, variant.QuietZone, variant.Flags, variant.TextByte, variant.LogoSizePercent, variant.LogoFlags, .. FuzzText.Encode(SeedTexts[kind]), .. logo]);
            }
        }

        // A version 6 QR code of level L: its error correction recovers the modules under a logo of 10%, not of 20% (the
        // unit tests of the cleared area)
        var longText = FuzzText.Encode(new string('a', 120));
        yield return new FuzzSeed("scene-0-logo-recovered", (byte[])[0, 1, 1, 6, 16, 0, 7, 2, .. longText, 0, .. GreenPng]);
        yield return new FuzzSeed("scene-0-logo-too-large", (byte[])[0, 1, 1, 6, 16, 0, 0, 0, .. longText, 0, .. GreenPng]);
        yield return new FuzzSeed("scene-0-logo-too-large-backdrop", (byte[])[0, 1, 1, 6, 16, 0, 0, 8, .. longText, 0, .. GreenPng]);

        // Wide and tall logos clear the band of their square they cover (the unit tests of non-square logos)
        foreach (var (width, height) in new[] { (64, 32), (32, 64), (50, 7) })
        {
            using var logoImage = new Image<Rgba32>(width, height, new Rgba32(0x25, 0x63, 0xeb));
            using var stream = new MemoryStream();
            logoImage.Save(stream, new PngEncoder());
            var url = FuzzText.Encode(SeedTexts[16]);
            yield return new FuzzSeed($"scene-16-logo-{width}x{height}", (byte[])[16, 2, 2, 6, 16, 0, 0, 0, .. url, 0, .. stream.ToArray()]);
            yield return new FuzzSeed($"scene-16-logo-{width}x{height}-backdrop", (byte[])[16, 2, 2, 6, 16, 0, 0, 8 | 2, .. url, 0, .. stream.ToArray()]);
        }

        // The layout of the digits of EAN and UPC with an add-on: the default text, a large font that shrinks to fit between
        // the guards, no quiet zone, which widens the drawing for the digits outside the bars, and fractional sizes
        (string Name, byte ModuleSize, byte ModuleHeight, byte QuietZone, byte Flags, byte TextByte)[] textVariants =
        [
            ("text", 1, 6, 12, 8, 0x00),
            ("text-large-font", 0, 6, 2, 8, 0x04),
            ("text-fractional", 4, 5, 3, 9, 0x2B),
        ];
        foreach (var (kind, text) in AddOnSeedTexts)
        {
            foreach (var variant in textVariants)
            {
                yield return new FuzzSeed($"scene-{kind}-addon-{variant.Name}", (byte[])[(byte)kind, variant.ModuleSize, variant.ModuleHeight, variant.QuietZone, variant.Flags, variant.TextByte, 0, 0, .. FuzzText.Encode(text), 0]);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetConsoleSeeds()
    {
        // The options of the unit tests: the defaults, no quiet zone, inverted colors, wider and taller modules, the text
        (string Name, byte Flags, byte QuietZone, byte ModuleWidth, byte ModuleHeight, byte BarHeight)[] variants =
        [
            ("default", 4, 0, 1, 1, 4),
            ("no-quiet-zone", 0, 1, 1, 1, 4),
            ("inverted", 1, 3, 1, 1, 4),
            ("scaled", 0, 2, 2, 3, 2),
            ("text", 2, 1, 1, 1, 1),
            ("text-inverted-default-quiet-zone", 7, 0, 3, 2, 5),
            ("invalid-module-width", 0, 1, 0, 1, 4),
            ("invalid-bar-height", 0, 1, 1, 1, 0),
            ("negative-quiet-zone", 0, 0, 1, 1, 4),
        ];

        for (var kind = 0; kind < KindCount; kind++)
        {
            foreach (var variant in variants)
            {
                yield return FuzzSeed.FromText($"console-{kind}-{variant.Name}", [(byte)kind, variant.Flags, variant.QuietZone, variant.ModuleWidth, variant.ModuleHeight, variant.BarHeight], SeedTexts[kind]);
            }
        }

        // Code 128 encodes the control characters: a new line and an escape sequence in the text under the bars
        yield return FuzzSeed.FromText("console-5-text-control-characters", [5, 2, 1, 1, 1, 1], "AB\nCD\u001b[2J");
    }
}
