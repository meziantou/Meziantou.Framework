using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/Imaging/11-Performance.md), run by DocumentationExampleTests.</summary>
internal static class PerformanceExamples
{
    /// <summary>A batch tool that processes one large image at a time lets a resize use several threads.</summary>
    #region Imaging_ParallelResize
    public static void ResizeWithAllCores(string inputPath, string outputPath)
    {
        // The configuration is captured by the loaded image: its resizes, transformations, convolutions and blurs split
        // large frames into bands processed concurrently. The result is the same as with one worker.
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Environment.ProcessorCount };

        using var image = Image.Load(inputPath, new ImageDecodeOptions { Configuration = configuration });
        image.Resize(new ResizeOptions(image.Width / 2, image.Height / 2) { Filter = ResamplingFilter.Lanczos3 });
        image.Save(outputPath);
    }
    #endregion

    /// <summary>A preview of an animation decodes its first frame only.</summary>
    #region Imaging_FirstFramePreview
    public static void SavePreview(string animationPath, string previewPath)
    {
        // Decoding stops after the first displayed frame: the rest of the file is not read
        using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

        image.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });

        // The image keeps its animation settings: save its frame as a still image
        image.Frames[0].Save(previewPath, new PngEncoder());
    }
    #endregion
}
