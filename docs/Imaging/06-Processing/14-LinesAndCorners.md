---
title: Lines and corners
description: Find the straight lines of an image with the Hough transform, and its corners with the Harris, Shi-Tomasi and FAST detectors.
---

Two analyses find geometric features in a frame without changing it:
[DetectLines](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectLines) returns its straight lines and
[DetectCorners](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectCorners) its corners. They work on one
frame: call them on `image.Frames[0]` for a still image.

## Find straight lines

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_DetectLines;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The detector is the Hough transform: the edges of the frame, found by the Canny detector of
[DetectCannyEdges](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectCannyEdges) with
[HoughLineOptions.EdgeOptions](cref:Meziantou.Framework.Imaging.HoughLineOptions.EdgeOptions), vote for the lines
through them whose direction matches the direction of the edge (within 30 degrees), and the lines with the most votes
win. A [HoughLine](cref:Meziantou.Framework.Imaging.HoughLine) is
infinite, in normal form: the points with `x cos θ + y sin θ = ρ`, where θ is
[Angle](cref:Meziantou.Framework.Imaging.HoughLine.Angle) and ρ is
[Distance](cref:Meziantou.Framework.Imaging.HoughLine.Distance), in the coordinates of the pixels (the center of the
pixel `(i, j)` is `(i + 0.5, j + 0.5)`, y grows downward). An angle of 0 is a vertical line, 90 a horizontal one.
[Slope](cref:Meziantou.Framework.Imaging.HoughLine.Slope) gives the direction of the line from the horizontal, and
[Votes](cref:Meziantou.Framework.Imaging.HoughLine.Votes) the number of edge pixels on it.

- **Resolution.** [AngleStep](cref:Meziantou.Framework.Imaging.HoughLineOptions.AngleStep) (1 degree by default) and
  [DistanceStep](cref:Meziantou.Framework.Imaging.HoughLineOptions.DistanceStep) (1 pixel) set the precision. A finer
  step costs time, and needs longer lines to collect the same votes.
- **Which lines.** [MinVotes](cref:Meziantou.Framework.Imaging.HoughLineOptions.MinVotes) is the shortest line (a quarter
  of the smaller side by default), and [MaxLines](cref:Meziantou.Framework.Imaging.HoughLineOptions.MaxLines) the number
  of lines. A straight edge also gives weaker votes to the lines a few degrees away: of two lines closer than
  [MinAngleSeparation](cref:Meziantou.Framework.Imaging.HoughLineOptions.MinAngleSeparation) and
  [MinDistanceSeparation](cref:Meziantou.Framework.Imaging.HoughLineOptions.MinDistanceSeparation) (5 degrees and 5 pixels
  by default), only the strongest is kept.
- **Noise.** Raise the blur of the Canny detector ([CannyEdgeOptions.Sigma](cref:Meziantou.Framework.Imaging.CannyEdgeOptions.Sigma))
  or its thresholds when textures give too many edges.

## Find corners

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_DetectCorners;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

A corner is a point where the brightness changes in every direction: the corner of an object, the crossing of two lines,
a spot. Corners are the points to track from frame to frame, or to match between two photos of the same scene, for
example to estimate the homography between them (see [Rotate, flip and transform](04-RotateFlipAndTransform.md)).
[CornerDetectionMethod](cref:Meziantou.Framework.Imaging.CornerDetectionMethod) chooses the detector:

| Detector | Response | Use it for |
| --- | --- | --- |
| [Harris](cref:Meziantou.Framework.Imaging.CornerDetectionMethod.Harris) (default) | `det(M) - k trace(M)²` of the structure tensor | General use |
| [ShiTomasi](cref:Meziantou.Framework.Imaging.CornerDetectionMethod.ShiTomasi) | The smaller eigenvalue of the structure tensor | Points to track |
| [Fast](cref:Meziantou.Framework.Imaging.CornerDetectionMethod.Fast) | 9 contiguous pixels of a circle of 16 brighter or darker than the center | Speed |

The structure tensor is made of the products of the gradients, smoothed by a Gaussian window of
[Sigma](cref:Meziantou.Framework.Imaging.CornerDetectionOptions.Sigma) (1.5 pixels by default). A corner is a local
maximum of the response above [ResponseThreshold](cref:Meziantou.Framework.Imaging.CornerDetectionOptions.ResponseThreshold)
times the strongest response of the frame, or, for FAST, a pixel whose circle is brighter or darker by
[FastThreshold](cref:Meziantou.Framework.Imaging.CornerDetectionOptions.FastThreshold) (a fraction of the largest
sample). The strongest corners are kept, at least [MinDistance](cref:Meziantou.Framework.Imaging.CornerDetectionOptions.MinDistance)
pixels apart, up to [MaxCorners](cref:Meziantou.Framework.Imaging.CornerDetectionOptions.MaxCorners). Each
[ImageCorner](cref:Meziantou.Framework.Imaging.ImageCorner) has its pixel and its response.

## What they read

Both analyses read the Rec. 709 luma of the frame, premultiplied by alpha for the corners, so a transparent area has no
corner. The results do not depend on the platform: the votes are integers, and equal votes or responses are sorted in a
fixed order. The working memory is charged to the image: for the lines, 18 bytes per pixel for the edges and 12 bytes
per cell of the accumulator; for the corners, up to 48 bytes per pixel.
