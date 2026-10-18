using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The canvas APIs without rendering backend (#379): the 2D context needs a backend (<c>getContext("2d")</c> returns null), the
/// paths, the colors and the PNG serialization are managed. The drawing is tested by the tests of the Imaging backend.
/// </summary>
public sealed class CanvasTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private string Run(string script)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => "<canvas id=c width=3 height=2></canvas><script>" + script + "</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void GetContext2D_WithoutBackend_IsNull()
    {
        var result = Run("""
            const canvas = document.getElementById('c');
            const offscreen = new OffscreenCanvas(2, 1);
            const path = new Path2D('M 0 0 L 10 0 L 10 10 Z');
            path.addPath(new Path2D(path), { e: 5 });
            document.title = [canvas.getContext('2d'), offscreen.getContext('2d'), typeof CanvasRenderingContext2D, canvas.toDataURL().slice(0, 22), canvas.toDataURL('image/jpeg').slice(0, 22)].join();
            """);

        Assert.Equal(",,function,data:image/png;base64,,data:image/png;base64,", result);
    }

    [Fact]
    public void PngEncoder_EncodesThePixelsRowByRow()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 10, 20, 30, 40];
        var png = PngEncoder.Encode(2, 2, pixels);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);

        // IHDR: 2x2, 8 bits, truecolor with alpha
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(6, png[25]);

        // The rows are unfiltered back to the pixels
        var idatLength = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var zlib = new ZLibStream(new MemoryStream(png, 41, idatLength), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var data = raw.ToArray();
        Assert.Equal(2 * ((2 * 4) + 1), data.Length);
        var decoded = new byte[16];
        for (var y = 0; y < 2; y++)
        {
            var filter = data[y * 9];
            for (var i = 0; i < 8; i++)
            {
                var left = i >= 4 ? decoded[(y * 8) + i - 4] : 0;
                var up = y > 0 ? decoded[((y - 1) * 8) + i] : 0;
                var upperLeft = y > 0 && i >= 4 ? decoded[((y - 1) * 8) + i - 4] : 0;
                var predictor = filter switch
                {
                    1 => left,
                    4 => Paeth(left, up, upperLeft),
                    _ => 0,
                };

                decoded[(y * 8) + i] = (byte)(data[(y * 9) + 1 + i] + predictor);
            }
        }

        Assert.Equal(pixels, decoded);

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    [Theory]
    [InlineData(0, 2 * Math.PI, false, 2 * Math.PI)]
    [InlineData(2 * Math.PI, 0, false, 2 * Math.PI)]
    [InlineData(0, 2 * Math.PI, true, -2 * Math.PI)]
    [InlineData(0, Math.PI / 2, false, Math.PI / 2)]
    [InlineData(0, Math.PI / 2, true, -3 * Math.PI / 2)]
    [InlineData(0, 0, false, 0)]
    public void Arc_SweepsLikeTheBrowsers(double startAngle, double endAngle, bool counterclockwise, double expectedSweep)
    {
        // A unit circle: the arc ends at the angle start + sweep, after one cubic Bézier curve per quarter turn
        var path = new CanvasPath();
        path.Arc(0, 0, 1, startAngle, endAngle, counterclockwise, DisplayMatrix.Identity);
        var curves = path.Verbs.Count(verb => verb == PathVerb.CubicTo);
        Assert.Equal((int)Math.Ceiling(Math.Round(Math.Abs(expectedSweep) / (Math.PI / 2), 6, MidpointRounding.AwayFromZero)), curves);
        var end = path.LastPoint;
        Assert.Equal(Math.Cos(startAngle + expectedSweep), end.X, 6);
        Assert.Equal(Math.Sin(startAngle + expectedSweep), end.Y, 6);
    }

    [Fact]
    public void RoundRect_ValidatesTheRadii()
    {
        Assert.Equal([(1d, 1d), (2d, 2d), (3d, 3d), (2d, 2d)], CanvasPath.NormalizeRoundRectRadii(new object?[] { 1d, 2d, 3d })!);
        Assert.Equal([(1d, 1d), (2d, 2d), (1d, 1d), (2d, 2d)], CanvasPath.NormalizeRoundRectRadii(new object?[] { 1d, 2d })!);
        Assert.Equal([(4d, 5d), (4d, 5d), (4d, 5d), (4d, 5d)], CanvasPath.NormalizeRoundRectRadii(new DomPointInit { X = 4, Y = 5 })!);
        Assert.Null(CanvasPath.NormalizeRoundRectRadii(new object?[] { 1d, double.NaN }));
        Assert.Throws<RangeErrorException>(() => CanvasPath.NormalizeRoundRectRadii(new object?[] { 1d, -1d }));
        Assert.Throws<RangeErrorException>(() => CanvasPath.NormalizeRoundRectRadii(Array.Empty<object?>()));
    }

    [Theory]
    [InlineData("red", "#ff0000")]
    [InlineData("rgba(255, 0, 255, 0)", "rgba(255, 0, 255, 0)")]
    [InlineData("rgb(none none none)", "#000000")]
    [InlineData("color(srgb 1 0 1)", "color(srgb 1 0 1)")]
    [InlineData("transparent", "rgba(0, 0, 0, 0)")]
    [InlineData("hsl(120 100% 50% / 0.5)", "rgba(0, 255, 0, 0.5)")]
    public void Colors_AreSerializedAsInTheBrowsers(string value, string expected)
    {
        Assert.Equal(expected, CanvasColors.Parse(value, element: null)?.Serialization);
    }
}
