using Meziantou.Framework.Imaging.Drawing;
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

    /// <summary>A poster drawn on a large canvas composites its large shapes on several threads.</summary>
    #region Imaging_ParallelDrawing
    public static void DrawPoster(string outputPath)
    {
        // The configuration of the image lets its draw calls composite large shapes on several threads.
        // The pixels are the same as with one thread.
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Environment.ProcessorCount };
        using var poster = new Image<Rgba32>(2400, 1600, new Rgba32(255, 255, 255), configuration);
        poster.Draw(canvas =>
        {
            canvas.Fill(new RadialGradientBrush(new PointD(1200, 800), 1400, [new GradientStop(0, new Rgba32(255, 200, 0)), new GradientStop(1, new Rgba32(0, 60, 160))]));
            canvas.FillEllipse(new SolidBrush(new Rgba32(255, 255, 255, 128)), new PointD(1200, 800), 900, 600);
        });
        poster.Save(outputPath);
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
