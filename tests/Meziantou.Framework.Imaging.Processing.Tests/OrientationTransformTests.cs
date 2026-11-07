using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Composition of the eight pixel permutations and their EXIF orientation, checked on a 3x2 grid of distinct cells. The
/// expected grids are written from the definition of the EXIF orientations (the position of the stored row 0 and column 0
/// in the displayed image), independently of <see cref="OrientationTransform.ForOrientation(ExifOrientation)"/>.
/// </summary>
public sealed class OrientationTransformTests
{
    // Stored pixels, 3 columns by 2 rows:
    //   a b c
    //   d e f
    private static readonly string[] Stored = ["abc", "def"];

    /// <summary>The stored grid displayed upright, for each EXIF orientation (TIFF 6.0 / EXIF "Orientation" tag).</summary>
    private static readonly Dictionary<ExifOrientation, string[]> Displayed = new()
    {
        [ExifOrientation.TopLeft] = ["abc", "def"], // 1: row 0 is the top, column 0 is the left side
        [ExifOrientation.TopRight] = ["cba", "fed"], // 2: row 0 is the top, column 0 is the right side
        [ExifOrientation.BottomRight] = ["fed", "cba"], // 3: row 0 is the bottom, column 0 is the right side
        [ExifOrientation.BottomLeft] = ["def", "abc"], // 4: row 0 is the bottom, column 0 is the left side
        [ExifOrientation.LeftTop] = ["ad", "be", "cf"], // 5: row 0 is the left side, column 0 is the top
        [ExifOrientation.RightTop] = ["da", "eb", "fc"], // 6: row 0 is the right side, column 0 is the top
        [ExifOrientation.RightBottom] = ["fc", "eb", "da"], // 7: row 0 is the right side, column 0 is the bottom
        [ExifOrientation.LeftBottom] = ["cf", "be", "ad"], // 8: row 0 is the left side, column 0 is the bottom
    };

    private static IEnumerable<OrientationTransform> AllTransforms
    {
        get
        {
            foreach (var transpose in new[] { false, true })
            {
                foreach (var flipX in new[] { false, true })
                {
                    foreach (var flipY in new[] { false, true })
                    {
                        yield return new OrientationTransform(transpose, flipX, flipY);
                    }
                }
            }
        }
    }

    [Fact]
    public void ExifOrientationIsTheOneTheTransformDisplaysUpright()
    {
        var seen = new HashSet<ExifOrientation>();
        foreach (var transform in AllTransforms)
        {
            var orientation = transform.ToExifOrientation();
            Assert.True(Displayed[orientation].AsSpan().SequenceEqual(Apply(Stored, transform)), $"{transform} reports {orientation} but displays {string.Join('/', Apply(Stored, transform))}.");
            Assert.True(seen.Add(orientation), $"{orientation} is reported by two transforms.");
        }

        Assert.HasCount(8, seen);
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft)]
    [InlineData(ExifOrientation.TopRight)]
    [InlineData(ExifOrientation.BottomRight)]
    [InlineData(ExifOrientation.BottomLeft)]
    [InlineData(ExifOrientation.LeftTop)]
    [InlineData(ExifOrientation.RightTop)]
    [InlineData(ExifOrientation.RightBottom)]
    [InlineData(ExifOrientation.LeftBottom)]
    public void ToExifOrientationIsTheInverseOfForOrientation(ExifOrientation orientation)
    {
        var transform = OrientationTransform.ForOrientation(orientation);
        Assert.Equal(orientation, transform.ToExifOrientation());
        Assert.Equal(Displayed[orientation], Apply(Stored, transform));
    }

    [Fact]
    public void MapRectangleToSourceSelectsTheStoredCellsThatARectangleOfTheResultShows()
    {
        var source = new Size(3, 2);
        foreach (var (orientation, displayed) in Displayed)
        {
            var transform = OrientationTransform.ForOrientation(orientation);
            var size = transform.GetOutputSize(source);
            Assert.Equal(new Size(displayed[0].Length, displayed.Length), size);

            // Every rectangle of the displayed grid shows the cells of one rectangle of the stored grid
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    for (var height = 1; y + height <= size.Height; height++)
                    {
                        for (var width = 1; x + width <= size.Width; width++)
                        {
                            var mapped = transform.MapRectangleToSource(source, new Rectangle(x, y, width, height));
                            Assert.Equal(width * height, mapped.Width * mapped.Height);
                            Assert.True(mapped.X >= 0 && mapped.Y >= 0 && mapped.Right <= source.Width && mapped.Bottom <= source.Height, $"{orientation}: {mapped} is not inside the stored grid");
                            var shown = Cells(displayed, new Rectangle(x, y, width, height));
                            var stored = Cells(Stored, mapped);
                            Assert.Equal(shown, stored);
                        }
                    }
                }
            }
        }

        // Orientation 6 displays the stored grid as "da" / "eb" / "fc": its right column is the stored row "abc"
        Assert.Equal(new Rectangle(0, 0, 3, 1), OrientationTransform.ForOrientation(ExifOrientation.RightTop).MapRectangleToSource(source, new Rectangle(1, 0, 1, 3)));

        // The cells of a rectangle, in a canonical order
        static string Cells(string[] grid, Rectangle rectangle)
        {
            var cells = new List<char>();
            for (var y = rectangle.Y; y < rectangle.Bottom; y++)
            {
                for (var x = rectangle.X; x < rectangle.Right; x++)
                {
                    cells.Add(grid[y][x]);
                }
            }

            cells.Sort();
            return new string([.. cells]);
        }
    }

    [Fact]
    public void ThenAppliesTheSecondTransformToTheResultOfTheFirst()
    {
        foreach (var first in AllTransforms)
        {
            foreach (var second in AllTransforms)
            {
                var expected = Apply(Apply(Stored, first), second);
                var actual = Apply(Stored, first.Then(second));
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{first} then {second}: {string.Join('/', actual)}, expected {string.Join('/', expected)}.");
            }
        }

        // Not commutative: a quarter turn then a mirror is not the mirror then the quarter turn
        var rotate = OrientationTransform.ForRotation(RotateMode.Rotate90);
        var mirror = OrientationTransform.ForFlip(FlipMode.Horizontal);
        Assert.Equal(ExifOrientation.LeftTop, rotate.Then(mirror).ToExifOrientation());
        Assert.Equal(ExifOrientation.RightBottom, mirror.Then(rotate).ToExifOrientation());
        Assert.Equal(OrientationTransform.Identity, rotate.Then(OrientationTransform.ForRotation(RotateMode.Rotate270)));
    }

    /// <summary>
    /// The AVIF transformative properties: <c>irot</c> rotates anti-clockwise by <paramref name="quarterTurns"/> x 90 degrees,
    /// then <c>imir</c> mirrors: axis 0 exchanges the top and the bottom, axis 1 the left and the right (-1: no <c>imir</c>).
    /// </summary>
    [Theory]
    [InlineData(0, -1, ExifOrientation.TopLeft)]
    [InlineData(1, -1, ExifOrientation.LeftBottom)]
    [InlineData(2, -1, ExifOrientation.BottomRight)]
    [InlineData(3, -1, ExifOrientation.RightTop)]
    [InlineData(0, 1, ExifOrientation.TopRight)]
    [InlineData(1, 1, ExifOrientation.RightBottom)]
    [InlineData(2, 1, ExifOrientation.BottomLeft)]
    [InlineData(3, 1, ExifOrientation.LeftTop)]
    [InlineData(0, 0, ExifOrientation.BottomLeft)]
    [InlineData(1, 0, ExifOrientation.LeftTop)]
    [InlineData(2, 0, ExifOrientation.TopRight)]
    [InlineData(3, 0, ExifOrientation.RightBottom)]
    public void AvifRotationThenMirrorMapsToExifOrientation(int quarterTurns, int mirrorAxis, ExifOrientation expected)
    {
        // An anti-clockwise rotation by k quarter turns is a clockwise rotation by 360 - 90k degrees
        var transform = OrientationTransform.ForRotation((RotateMode)((360 - (90 * quarterTurns)) % 360));
        if (mirrorAxis >= 0)
        {
            transform = transform.Then(OrientationTransform.ForFlip(mirrorAxis is 0 ? FlipMode.Vertical : FlipMode.Horizontal));
        }

        Assert.Equal(expected, transform.ToExifOrientation());

        // The same result from the pixels: rotate the grid anti-clockwise by hand, mirror it, and find the orientation that displays it
        var grid = Stored;
        for (var i = 0; i < quarterTurns; i++)
        {
            grid = RotateAntiClockwise(grid);
        }

        grid = mirrorAxis switch
        {
            0 => [.. Enumerable.Reverse(grid)],
            1 => [.. grid.Select(row => string.Concat(Enumerable.Reverse(row)))],
            _ => grid,
        };
        Assert.Equal(Displayed[expected], grid);
    }

    /// <summary>The top row of the result is the right column of the source, read from top to bottom.</summary>
    private static string[] RotateAntiClockwise(string[] grid)
    {
        var width = grid[0].Length;
        return [.. Enumerable.Range(0, width).Select(y => string.Concat(grid.Select(row => row[width - 1 - y])))];
    }

    /// <summary>
    /// Applies a transform with the mapping the type documents, from destination to source coordinates:
    /// <c>(u, v) = Transpose ? (dy, dx) : (dx, dy)</c>, then <c>sx = FlipX ? W - 1 - u : u</c> and <c>sy = FlipY ? H - 1 - v : v</c>.
    /// </summary>
    private static string[] Apply(string[] source, OrientationTransform transform)
    {
        var sourceWidth = source[0].Length;
        var sourceHeight = source.Length;
        var size = transform.GetOutputSize(new Size(sourceWidth, sourceHeight));
        Assert.Equal(transform.Transpose ? new Size(sourceHeight, sourceWidth) : new Size(sourceWidth, sourceHeight), size);
        var result = new string[size.Height];
        for (var dy = 0; dy < size.Height; dy++)
        {
            var row = new char[size.Width];
            for (var dx = 0; dx < size.Width; dx++)
            {
                var (u, v) = transform.Transpose ? (dy, dx) : (dx, dy);
                var sx = transform.FlipX ? sourceWidth - 1 - u : u;
                var sy = transform.FlipY ? sourceHeight - 1 - v : v;
                row[dx] = source[sy][sx];
            }

            result[dy] = new string(row);
        }

        return result;
    }
}
