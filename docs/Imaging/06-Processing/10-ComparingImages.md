---
title: Comparing images
description: Measure how similar two images look with SSIM, PSNR, SSIMULACRA 2 and Butteraugli, and find similar images with perceptual hashes.
---

[ImageSimilarity](cref:Meziantou.Framework.Imaging.ImageSimilarity) measures how similar two frames look: for visual regression tests, deduplication or quality checks.
It never changes the frames.

## Check that two images look the same

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ImageSimilarity;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool LookTheSame(string expectedPath, string actualPath)
{
    using var expected = Image.Load(expectedPath);
    using var actual = Image.Load(actualPath);
    if (expected.Size != actual.Size || expected.Frames.Count != actual.Frames.Count)
        return false;

    for (var i = 0; i < expected.Frames.Count; i++)
    {
        // 1 for identical pixels; small differences, such as anti-aliasing, keep the score close to 1
        if (ImageSimilarity.ComputeSsim(expected.Frames[i], actual.Frames[i]) < 0.99)
            return false;

        // 0 for the same structure and brightness, up to 64; the pHash is the most tolerant to small changes
        if (ImageSimilarity.ComputePerceptualHashDistance(expected.Frames[i], actual.Frames[i]) > 4)
            return false;

        // Perceptual metrics: 100 for identical pixels, about 90 when the difference is not visible
        if (ImageSimilarity.ComputeSsimulacra2(expected.Frames[i], actual.Frames[i]) < 90)
            return false;

        // 0 for identical pixels, below about 1 when the difference is not visible, even in a small area
        if (ImageSimilarity.ComputeButteraugliDistance(expected.Frames[i], actual.Frames[i]).MaxDistance > 1)
            return false;
    }

    return true;
}
```
<!-- endref -->

The members compare frames: compare an animation frame by frame, as this example does.

## Similarity scores

| Method | Result | Identical pixels | Notes |
| --- | --- | --- | --- |
| [ComputeSsim](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeSsim) | The mean Structural Similarity Index, from -1 to 1 | 1 | A localized difference lowers the score in proportion to its area |
| [ComputePsnr](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputePsnr) | The peak signal-to-noise ratio, in decibels | Infinity | Computed from the mean squared error: higher is better |
| [ComputeSsimulacra2](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeSsimulacra2) | The SSIMULACRA 2 score, a perceptual metric | 100 | About 90 when the difference is not visible, 70 for a high quality, 50 for a medium quality, negative for very different frames. The frames must be at least 8x8 pixels |
| [ComputeButteraugliDistance](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeButteraugliDistance) | A [ButteraugliDistance](cref:Meziantou.Framework.Imaging.ButteraugliDistance): [MaxDistance](cref:Meziantou.Framework.Imaging.ButteraugliDistance.MaxDistance), and [PNorm3](cref:Meziantou.Framework.Imaging.ButteraugliDistance.PNorm3), a summary of the whole frame | 0 | [MaxDistance](cref:Meziantou.Framework.Imaging.ButteraugliDistance.MaxDistance) below about 1 when the difference is not visible, even in a small area. The slowest member |

[ComputeSsim](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeSsim), [ComputePsnr](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputePsnr), [ComputeSsimulacra2](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeSsimulacra2) and [ComputeButteraugliDistance](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeButteraugliDistance) compare two frames of the same size.
SSIMULACRA 2 (tuned on subjective scores) and Butteraugli (a psychovisual model) follow how people judge image
quality: use them to check that a compressed or processed image keeps the look of its original. SSIM and PSNR measure
the differences of the pixels, with the definitions of scikit-image.

## Find similar images with hashes

[ComputeDifferenceHash](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeDifferenceHash) (dHash) and [ComputePerceptualHash](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputePerceptualHash) (pHash) return a 64-bit hash of the structure of a frame.
Compute the hash of every image once and compare the hashes: the number of bits that differ is small for a resized
copy, and large for another picture:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_FindSimilarImages;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The hashes are computed from a luminance thumbnail of the frame, so frames of different sizes have comparable hashes.
Every pixel counts, and differences smaller than a luminance level never set a bit. The hashes ignore the
brightness.

[ComputeDifferenceHashDistance](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputeDifferenceHashDistance) and [ComputePerceptualHashDistance](cref:Meziantou.Framework.Imaging.ImageSimilarity.ComputePerceptualHashDistance) compare two frames of the same size directly, from
0 to [ImageSimilarity.MaxHashDistance](cref:Meziantou.Framework.Imaging.ImageSimilarity.MaxHashDistance) (64): the number of hash bits that differ, plus the difference between the mean
luminances of the frames, so that the brightness counts too.

## Transparency and pixel formats

Every pixel format is read as 8-bit RGBA (16-bit samples are rounded), so frames of different pixel formats can be
compared. Transparency is taken into account:

- SSIM and PSNR compare the colors premultiplied by alpha (a fully transparent pixel scores the same whatever color it
  hides), score alpha on its own, and keep the lower score.
- SSIMULACRA 2 composites frames that are not fully opaque over a dark and a bright gray, and keeps the lower score.
- Butteraugli composites them over black and over white, and keeps the larger distance of every pixel.
- The hash distances compare them over a black and over a white background, and keep the larger distance.

The results depend only on the pixels. The last digits of PSNR, SSIMULACRA 2 and Butteraugli can differ between
platforms, since they use the logarithm and power functions of the platform: compare them with a threshold, never for
equality.
