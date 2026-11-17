using System.Numerics;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/Imaging/06-Processing), run by DocumentationExampleTests.</summary>
internal static class ProcessingExamples
{
    /// <summary>Processes one frame of an animation and leaves the other frames as they are.</summary>
    #region Imaging_ProcessOneFrame
    public static void BlurFirstFrame(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // A frame is a view of its image: the blur changes this frame only, the other frames and the timing are kept
        image.Frames[0].GaussianBlur(2);
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Crops the centered square of an image, then places it on a larger canvas.</summary>
    #region Imaging_CropAndExtendCanvas
    public static void CropAndExtendCanvas(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The largest centered square. The rectangle must be entirely inside the canvas: it is never clamped
        var side = Math.Min(image.Width, image.Height);
        image.Crop(new Rectangle((image.Width - side) / 2, (image.Height - side) / 2, side, side));

        // A canvas twice as wide, the square on its left. The new area is opaque white, which every pixel format stores
        image.ExtendCanvas(new Size(side * 2, side), ResizeAnchor.Left, Colors.White);
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Fills a box exactly: the image covers the box and what overflows is cut around an anchor.</summary>
    #region Imaging_ResizeToFill
    public static void ResizeToFill(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Exactly 200x200: the image is scaled to cover the box and the overflow is cut. The top anchor keeps the top of
        // a portrait, and cuts its bottom
        image.Resize(new ResizeOptions(200, 200) { Mode = ResizeMode.Cover, Anchor = ResizeAnchor.Top });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Content-aware resizing: the background shrinks, the subjects keep their proportions.</summary>
    #region Imaging_SeamCarve
    public static void ResizeToBanner(string inputPath, string outputPath, Rectangle face)
    {
        using var image = Image.Load(inputPath);

        // Half as wide: the seams of least detail are removed, and never through the face while another seam remains
        var protectedRegion = new PixelMask(image.Size);
        protectedRegion.Fill(face);
        image.SeamCarve(image.Width / 2, image.Height, new SeamCarvingOptions { ProtectedRegion = protectedRegion });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Removes what a mask covers: scratches, a caption, a small object.</summary>
    #region Imaging_Inpaint
    public static void RemoveScratches(string photoPath, string maskPath, string outputPath)
    {
        using var photo = Image.Load(photoPath);

        // The mask is painted in white on black, at the size of the photo
        PixelMask mask;
        using (var maskImage = Image.Load(maskPath))
        {
            mask = PixelMask.FromFrame(maskImage.Frames[0]);
        }

        // The pixels of the mask are filled from the pixels around them; a larger radius gives smoother fills
        photo.Inpaint(mask, new InpaintingOptions { Radius = 4 });
        photo.Save(outputPath);
    }
    #endregion

    /// <summary>Finds the straight lines of a photo, such as the edges of a document or the lines of a court.</summary>
    #region Imaging_DetectLines
    public static IReadOnlyList<HoughLine> FindStraightLines(string inputPath)
    {
        using var photo = Image.Load(inputPath);

        // The 8 strongest lines of the first frame: x cos(Angle) + y sin(Angle) = Distance, in pixels
        var lines = photo.Frames[0].DetectLines(new HoughLineOptions { MaxLines = 8 });
        foreach (var line in lines)
        {
            Console.WriteLine($"{line.Slope:F1} degrees from horizontal, {line.Votes} edge pixels");
        }

        return lines;
    }
    #endregion

    /// <summary>Finds the corners of a photo, to track or to match them.</summary>
    #region Imaging_DetectCorners
    public static IReadOnlyList<ImageCorner> FindCorners(string inputPath)
    {
        using var photo = Image.Load(inputPath);

        // The 100 strongest Shi-Tomasi corners, at least 8 pixels apart
        return photo.Frames[0].DetectCorners(new CornerDetectionOptions
        {
            Method = CornerDetectionMethod.ShiTomasi,
            MaxCorners = 100,
            MinDistance = 8,
        });
    }
    #endregion

    /// <summary>Straightens a page scanned at a slight angle.</summary>
    #region Imaging_Deskew
    public static double StraightenScan(string inputPath, string outputPath)
    {
        using var scan = Image.Load(inputPath);

        // The angle of the lines of text is estimated, and the page rotated back; the corners it uncovers are white
        var skew = scan.Deskew(new SkewEstimationOptions { MaxAngle = 10 });
        scan.Save(outputPath);
        return skew;
    }
    #endregion

    /// <summary>Warps a photo onto the plane of matched points, ignoring the wrong matches.</summary>
    #region Imaging_EstimateHomography
    public static HomographyEstimate AlignToReference(string photoPath, string outputPath, PointD[] photoPoints, PointD[] referencePoints, Size referenceSize)
    {
        using var photo = Image.Load(photoPath);

        // The points of the photo and the points of the reference they match, some of them wrong: RANSAC keeps the
        // homography most of them agree on
        var estimate = ProjectiveTransform.EstimateHomographyRobust(photoPoints, referencePoints, new HomographyRansacOptions { Threshold = 2 });

        // The photo seen from the point of view of the reference
        photo.Transform(estimate.Transform, new TransformOptions
        {
            CanvasMode = TransformCanvasMode.Explicit,
            CanvasSize = referenceSize,
            BackgroundColor = new Rgba64(0, 0, 0, 65535),
        });
        photo.Save(outputPath);
        return estimate;
    }
    #endregion

    /// <summary>Quarter turns and mirrors: exact pixel permutations.</summary>
    #region Imaging_RotateAndFlip
    public static void RotateAndFlip(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Make the stored pixels upright first: a camera stores how to turn the photo in its EXIF orientation
        image.AutoOrient();

        // A clockwise quarter turn, then a mirror: every pixel is moved, none is resampled
        image.Rotate(RotateMode.Rotate90);
        image.Flip(FlipMode.Horizontal);
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A skew, and an affine transformation that combines a scaling and a rotation.</summary>
    #region Imaging_SkewAndTransform
    public static void SkewAndTransform(string inputPath, string skewedPath, string transformedPath)
    {
        using var image = Image.Load(inputPath);

        // The vertical lines lean by 20 degrees. The canvas grows to hold the whole image, and the area the image does
        // not cover is transparent: a pixel format without alpha needs an opaque TransformOptions.BackgroundColor
        using (var skewed = image.Clone())
        {
            skewed.Skew(20, 0);
            skewed.Save(skewedPath);
        }

        // Half the size, then a 45-degree turn: the transformations are combined first, so the pixels are resampled once
        var transform = AffineTransform.CreateScale(0.5, 0.5).Then(AffineTransform.CreateRotation(45));
        image.Transform(transform, new TransformOptions { Filter = ResamplingFilter.Lanczos3 });
        image.Save(transformedPath);
    }
    #endregion

    /// <summary>Makes a plain background transparent.</summary>
    #region Imaging_ReplaceColor
    public static void MakeBackgroundTransparent(string inputPath, string outputPath)
    {
        // Load the pixels with an alpha channel: a JPEG image has none, and could not store the transparency
        using var image = Image.Load<Rgba32>(inputPath);

        // Every pixel within 4 levels of white on each sample (JPEG noise) becomes transparent
        image.ReplaceColor(Colors.White, Colors.Transparent, tolerance: 4.0 / 255);
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Dithers an image to black and white, with a fixed palette of two colors.</summary>
    #region Imaging_DitherToBlackAndWhite
    public static void DitherToBlackAndWhite(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Only the colors of the palette are used. Error diffusion turns the gray levels into patterns of black and white
        // pixels, where a threshold would make them all black or all white
        var blackAndWhite = new ImagePalette([Colors.Black, Colors.White]);
        image.Quantize(new QuantizeOptions { Palette = blackAndWhite, Dithering = DitheringMode.FloydSteinberg });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>Finds the images that look like a reference image, from their perceptual hashes.</summary>
    #region Imaging_FindSimilarImages
    public static IReadOnlyList<string> FindSimilarImages(string referencePath, IEnumerable<string> candidatePaths)
    {
        // A 64-bit hash of the structure of a frame: store it to compare an image with many others without reloading it
        ulong referenceHash;
        using (var reference = Image.Load(referencePath))
        {
            referenceHash = ImageSimilarity.ComputePerceptualHash(reference.Frames[0]);
        }

        var result = new List<string>();
        foreach (var path in candidatePaths)
        {
            using var candidate = Image.Load(path);
            var hash = ImageSimilarity.ComputePerceptualHash(candidate.Frames[0]);

            // The number of the 64 bits that differ. Frames of different sizes can be compared: a resized copy differs by
            // a few bits, another picture by many more
            if (BitOperations.PopCount(referenceHash ^ hash) <= 10)
            {
                result.Add(path);
            }
        }

        return result;
    }
    #endregion
}
