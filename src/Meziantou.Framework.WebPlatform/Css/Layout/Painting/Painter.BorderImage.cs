using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The border images (https://drafts.csswg.org/css-backgrounds/#border-images).
internal sealed partial class Painter
{
    /// <summary>
    /// Paints the border image of a box instead of its border styles (https://drafts.csswg.org/css-backgrounds/#border-images):
    /// the image is sliced in nine regions (<c>border-image-slice</c>), drawn in the regions of the border image area (the border
    /// box extended by <c>border-image-outset</c>, the widths of <c>border-image-width</c>): the corners are scaled, the sides
    /// are stretched, repeated, rounded or spaced (<c>border-image-repeat</c>), the middle is drawn with <c>fill</c>. Returns
    /// false when the box has no border image (none, or an image that is not available): its borders are painted.
    /// </summary>
    private bool PaintBorderImage(BoxFragment fragment, ComputedStyle style, PhysicalRect borderBox, string prefix = "border-image")
    {
        var source = style.GetComputedValue(prefix + "-source");
        if (source is "none" or "")
            return false;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(source));
        if (values.Count == 0)
            return false;

        if (PaintedImage.Resolve(_document, style, values[0], this) is not { } image)
        {
            // A fetched image that is not painted (an SVG image, #377) still replaces the border styles
            return Om.CssImageFetching.GetNaturalSize(_document, source) is { Width: > 0, Height: > 0 };
        }

        var zoom = style.Layout.EffectiveZoom;
        var border = fragment.Border;

        // https://drafts.csswg.org/css-backgrounds/#border-image-outset
        var outset = ParseFourValues(style.GetComputedValue(prefix + "-outset"), (value, side) =>
            value.Number is { } number ? number * GetSide(border, side) : value.Length?.Resolve(0) ?? 0, zoom);
        var area = new PhysicalRect(borderBox.X - outset.Left, borderBox.Y - outset.Top, borderBox.Width + outset.Left + outset.Right, borderBox.Height + outset.Top + outset.Bottom);
        if (area.IsEmpty)
            return true;

        // The size of the image: its natural size, or the border image area for an image without natural dimensions
        var imageWidth = image.NaturalWidth ?? area.Width;
        var imageHeight = image.NaturalHeight ?? area.Height;
        if (image.NaturalRatio is { } ratio && image.NaturalWidth is null)
        {
            imageHeight = imageWidth / ratio;
        }

        if (imageWidth <= 0 || imageHeight <= 0)
            return true;

        // https://drafts.csswg.org/css-backgrounds/#border-image-slice: the numbers are pixels of a raster image
        var sliceValue = style.GetComputedValue(prefix + "-slice");
        var fill = sliceValue.Contains("fill", StringComparison.Ordinal);
        var pixelScale = image is PaintedImage.RasterImage raster ? imageWidth / raster.Image.Width : 1;
        var slices = ParseFourValues(sliceValue.Replace("fill", "", StringComparison.Ordinal), (value, side) =>
        {
            var basis = side is PhysicalSide.Top or PhysicalSide.Bottom ? imageHeight : imageWidth;
            var slice = value.Number is { } number ? number * pixelScale : value.Length?.Resolve(basis) ?? 0;
            return Math.Clamp(slice, 0, basis);
        }, zoom: 1, percentagesOnly: true);

        // https://drafts.csswg.org/css-backgrounds/#border-image-width: "auto: the border image width is the intrinsic width or
        // height (whichever is applicable) of the corresponding image slice". As in the browsers, this size is not zoomed (the
        // natural size of the image is, so are the slices: the zoom does not change the part of the image in a slice)
        var naturalZoom = image.NaturalWidth is not null || image.NaturalHeight is not null ? zoom : 1;
        var widths = ParseFourValues(style.GetComputedValue(prefix + "-width"), (value, side) =>
        {
            if (value.IsAuto)
                return GetSide(slices, side) / naturalZoom;

            var basis = side is PhysicalSide.Top or PhysicalSide.Bottom ? area.Height : area.Width;
            return Math.Max(0, value.Number is { } number ? number * GetSide(border, side) : value.Length?.Resolve(basis) ?? 0);
        }, zoom);

        // "If two opposite border-image-width offsets are large enough that they overlap, the used values of all
        // border-image-width offsets are proportionally reduced until they no longer overlap"
        var factor = Math.Min(1, Math.Min(widths.Left + widths.Right > 0 ? area.Width / (widths.Left + widths.Right) : 1, widths.Top + widths.Bottom > 0 ? area.Height / (widths.Top + widths.Bottom) : 1));
        if (factor < 1)
        {
            widths = new Sides<double>(widths.Top * factor, widths.Right * factor, widths.Bottom * factor, widths.Left * factor);
        }

        var repeatParts = style.GetComputedValue(prefix + "-repeat").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var repeatX = repeatParts.Length > 0 ? repeatParts[0] : "stretch";
        var repeatY = repeatParts.Length > 1 ? repeatParts[1] : repeatX;
        var content = image.CreateShader(new PhysicalSize(imageWidth, imageHeight));
        if (content is null)
            return true;

        // The slice lines of the image and the region lines of the area
        double[] sx = [0, slices.Left, imageWidth - slices.Right, imageWidth];
        double[] sy = [0, slices.Top, imageHeight - slices.Bottom, imageHeight];
        // (the lines between the regions are on device pixels: two regions do not share an antialiased edge, which would let
        // what is below the border image show through)
        double[] dx = [area.Left, SnapToDevicePixels(area.Left + widths.Left), SnapToDevicePixels(area.Right - widths.Right), area.Right];
        double[] dy = [area.Top, SnapToDevicePixels(area.Top + widths.Top), SnapToDevicePixels(area.Bottom - widths.Bottom), area.Bottom];
        var element = fragment.Box.Element;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1 && !fill)
                    continue;

                var sourceRect = new PhysicalRect(sx[column], sy[row], sx[column + 1] - sx[column], sy[row + 1] - sy[row]);
                var destination = new PhysicalRect(dx[column], dy[row], dx[column + 1] - dx[column], dy[row + 1] - dy[row]);
                if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destination.Width <= 0 || destination.Height <= 0)
                    continue;

                // The scale of the tiles: the corners fill their region; a side is scaled to the width of its border (the
                // middle uses the scales of the top and left sides), then repeated along its length
                double tileWidth, tileHeight;
                if (column == 1 && row != 1)
                {
                    var scale = destination.Height / sourceRect.Height;
                    tileWidth = repeatX == "stretch" ? destination.Width : sourceRect.Width * scale;
                    tileHeight = destination.Height;
                }
                else if (row == 1 && column != 1)
                {
                    var scale = destination.Width / sourceRect.Width;
                    tileWidth = destination.Width;
                    tileHeight = repeatY == "stretch" ? destination.Height : sourceRect.Height * scale;
                }
                else if (row == 1 && column == 1)
                {
                    var scaleX = widths.Top > 0 && slices.Top > 0 ? widths.Top / slices.Top : widths.Bottom > 0 && slices.Bottom > 0 ? widths.Bottom / slices.Bottom : 1;
                    var scaleY = widths.Left > 0 && slices.Left > 0 ? widths.Left / slices.Left : widths.Right > 0 && slices.Right > 0 ? widths.Right / slices.Right : 1;
                    tileWidth = repeatX == "stretch" ? destination.Width : sourceRect.Width * scaleX;
                    tileHeight = repeatY == "stretch" ? destination.Height : sourceRect.Height * scaleY;
                }
                else
                {
                    tileWidth = destination.Width;
                    tileHeight = destination.Height;
                }

                var horizontal = column == 1 ? repeatX : "stretch";
                var vertical = row == 1 ? repeatY : "stretch";
                if (!GetTiling(horizontal, destination.X, destination.Width, ref tileWidth, out var originX, out var spacingX, out var repeatsX)
                    || !GetTiling(vertical, destination.Y, destination.Height, ref tileHeight, out var originY, out var spacingY, out var repeatsY))
                {
                    continue;
                }

                // The content of a tile: the slice of the image scaled to the tile
                var tileContent = content with
                {
                    LocalTransform = DisplayMatrix.Scale(tileWidth / sourceRect.Width, tileHeight / sourceRect.Height)
                        .PreConcat(DisplayMatrix.Translation(-sourceRect.X, -sourceRect.Y))
                        .PreConcat(content.LocalTransform ?? DisplayMatrix.Identity),
                };
                if (tileContent is ImageShader imageShader)
                {
                    // Only the pixels of the slice are sampled: the scaled slice has no color of the slices next to it at its edges
                    var pixelScaleX = imageShader.Image.Width / imageWidth;
                    var pixelScaleY = imageShader.Image.Height / imageHeight;
                    tileContent = imageShader with { SourceRect = new PhysicalRect(RoundPixel(sourceRect.X * pixelScaleX), RoundPixel(sourceRect.Y * pixelScaleY), RoundPixel(sourceRect.Width * pixelScaleX), RoundPixel(sourceRect.Height * pixelScaleY)) };
                }
                else if (content is DisplayListShader { LocalTransform: null } vector)
                {
                    // An image painted by a display list (an SVG image) is painted again for the tile, scaled and clipped to the
                    // slice: the tile is rasterized at its size (the image rasterized once, then scaled to the tile, would have
                    // the colors of the slices next to it at its edges)
                    var tileSize = new PhysicalSize(tileWidth, tileHeight);
                    var tileBuilder = new DisplayListBuilder();
                    tileBuilder.Add(new ClipRectItem(new PhysicalRect(0, 0, tileWidth, tileHeight)));
                    tileBuilder.Transform(DisplayMatrix.Scale(tileWidth / sourceRect.Width, tileHeight / sourceRect.Height).PreConcat(DisplayMatrix.Translation(-sourceRect.X, -sourceRect.Y)));
                    tileBuilder.AddList(vector.Content);
                    tileContent = new DisplayListShader(tileBuilder.ToDisplayList(tileSize), tileSize);
                }

                var shader = new TiledShader(tileContent, new PhysicalSize(tileWidth, tileHeight), new PhysicalSize(spacingX, spacingY), repeatsX ? SpreadMode.Repeat : SpreadMode.Decal, repeatsY ? SpreadMode.Repeat : SpreadMode.Decal)
                {
                    LocalTransform = DisplayMatrix.Translation(originX, originY),
                };
                _builder.Add(new DrawRectItem(destination, new DisplayPaint(DisplayColor.Black) { Shader = shader }) { Source = element });
            }
        }

        return true;
    }

    /// <summary>
    /// Computes the tiling of a region of a border image along an axis (https://drafts.csswg.org/css-backgrounds/#border-image-repeat):
    /// stretch fills the region with one tile, repeat centers the tiles, round adjusts the tile size to a whole number of tiles,
    /// space spreads the whole tiles that fit with equal gaps. Returns false when nothing is drawn.
    /// </summary>
    private static bool GetTiling(string mode, double start, double length, ref double tile, out double origin, out double spacing, out bool repeats)
    {
        spacing = 0;
        origin = start;
        repeats = false;
        switch (mode)
        {
            case "repeat":
                repeats = true;
                origin = start + ((length - tile) / 2);
                return tile > 0;

            case "round":
            {
                var count = Math.Max(1, Math.Round(length / tile, MidpointRounding.AwayFromZero));
                tile = length / count;
                repeats = true;
                return tile > 0;
            }

            case "space":
            {
                var count = Math.Floor((length / tile) + 1e-9);
                if (count < 1)
                    return false;

                spacing = (length - (count * tile)) / (count + 1);
                origin = start + spacing;
                repeats = true;
                return true;
            }

            default:
                tile = length;
                return tile > 0;
        }
    }

    /// <summary>Returns a coordinate in image pixels without the rounding error of its computation.</summary>
    private static double RoundPixel(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(value - rounded) < 1e-6 ? rounded : value;
    }

    private static double GetSide(Sides<double> sides, PhysicalSide side) => side switch
    {
        PhysicalSide.Top => sides.Top,
        PhysicalSide.Right => sides.Right,
        PhysicalSide.Bottom => sides.Bottom,
        _ => sides.Left,
    };

    /// <summary>A component of a four-value property of the border images: a number, a length-percentage or auto.</summary>
    private readonly record struct FourValue(double? Number, LengthPercentage? Length, bool IsAuto);

    /// <summary>
    /// Parses a property of 1 to 4 values (top, right, bottom, left, the missing ones copied as for <c>margin</c>) and resolves
    /// each value for its side.
    /// </summary>
    private static Sides<double> ParseFourValues(string value, Func<FourValue, PhysicalSide, double> resolve, double zoom, bool percentagesOnly = false)
    {
        var parsed = new List<FourValue>(4);
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Number } number:
                    parsed.Add(new FourValue(number.Token.NumericValue, null, IsAuto: false));
                    break;

                case CssPreservedToken { Type: CssTokenType.Ident } ident when Infra.InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "auto"):
                    parsed.Add(new FourValue(null, null, IsAuto: true));
                    break;

                default:
                    if (LengthPercentage.TryParse(component, zoom, nonNegative: false, out var length) && (!percentagesOnly || length.HasPercentage))
                    {
                        parsed.Add(new FourValue(null, length, IsAuto: false));
                    }

                    break;
            }

            if (parsed.Count == 4)
                break;
        }

        if (parsed.Count == 0)
            return default;

        var top = parsed[0];
        var right = parsed.Count > 1 ? parsed[1] : top;
        var bottom = parsed.Count > 2 ? parsed[2] : top;
        var left = parsed.Count > 3 ? parsed[3] : right;
        return new Sides<double>(resolve(top, PhysicalSide.Top), resolve(right, PhysicalSide.Right), resolve(bottom, PhysicalSide.Bottom), resolve(left, PhysicalSide.Left));
    }
}
