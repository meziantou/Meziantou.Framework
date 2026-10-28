namespace Meziantou.Framework.Imaging;

/// <summary>Selects the operator of <see cref="ImageProcessingExtensions.DetectEdges(Image, EdgeDetector, bool, ConvolutionEdgeMode, CancellationToken)"/>.</summary>
public enum EdgeDetector
{
    /// <summary>
    /// The magnitude <c>sqrt(gx^2 + gy^2)</c> of the gradients of <see cref="ConvolutionKernel.SobelX"/> and
    /// <see cref="ConvolutionKernel.SobelY"/>.
    /// </summary>
    Sobel = 0,

    /// <summary>
    /// The magnitude <c>sqrt(gx^2 + gy^2)</c> of the gradients of <see cref="ConvolutionKernel.ScharrX"/> and
    /// <see cref="ConvolutionKernel.ScharrY"/>.
    /// </summary>
    Scharr = 1,

    /// <summary>
    /// The magnitude <c>sqrt(gx^2 + gy^2)</c> of the gradients of <see cref="ConvolutionKernel.PrewittX"/> and
    /// <see cref="ConvolutionKernel.PrewittY"/>.
    /// </summary>
    Prewitt = 2,

    /// <summary>
    /// The largest response of the eight Kirsch compass matrices: <c>[5, 5, 5; -3, 0, -3; -3, -3, -3]</c> (north) and its
    /// seven rotations by 45 degrees, made by moving the eight outer weights around the middle one.
    /// </summary>
    Kirsch = 3,

    /// <summary>
    /// The largest response of the eight Robinson compass matrices: <see cref="ConvolutionKernel.SobelX"/> and its seven
    /// rotations by 45 degrees, made by moving the eight outer weights around the middle one. Each matrix is the negation of
    /// the opposite one, so the largest response is the largest absolute response of four of them.
    /// </summary>
    Robinson = 4,

    /// <summary>The absolute value of the response of <see cref="ConvolutionKernel.Laplacian3x3"/>.</summary>
    Laplacian3x3 = 5,

    /// <summary>The absolute value of the response of <see cref="ConvolutionKernel.Laplacian5x5"/>.</summary>
    Laplacian5x5 = 6,

    /// <summary>The absolute value of the response of <see cref="ConvolutionKernel.LaplacianOfGaussian"/>.</summary>
    LaplacianOfGaussian = 7,
}
