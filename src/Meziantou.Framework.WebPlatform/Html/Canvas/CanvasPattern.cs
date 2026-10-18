using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A pattern of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvaspattern): an image repeated in the
/// directions of its repetition behavior, created by <c>createPattern()</c>. The image is a snapshot of the source when the
/// pattern is created (later changes of a canvas source are not reflected).
/// </summary>
[IdlInterface("CanvasPattern", "https://html.spec.whatwg.org/multipage/canvas.html#canvaspattern")]
public sealed class CanvasPattern
{
    private readonly PaintImage _image;
    private readonly SpreadMode _spreadX;
    private readonly SpreadMode _spreadY;
    private DisplayMatrix _transform = DisplayMatrix.Identity;

    internal CanvasPattern(PaintImage image, string repetition, bool originClean)
    {
        _image = image;
        IsOriginClean = originClean;
        (_spreadX, _spreadY) = repetition switch
        {
            "repeat-x" => (SpreadMode.Repeat, SpreadMode.Decal),
            "repeat-y" => (SpreadMode.Decal, SpreadMode.Repeat),
            "no-repeat" => (SpreadMode.Decal, SpreadMode.Decal),
            _ => (SpreadMode.Repeat, SpreadMode.Repeat),
        };
    }

    /// <summary>Whether the image of the pattern does not come from another origin (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvaspattern-origin-clean).</summary>
    internal bool IsOriginClean { get; }

    /// <summary>Whether a repetition behavior is valid (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createpattern): the empty string is <c>repeat</c>.</summary>
    internal static bool IsValidRepetition(string repetition) => repetition is "" or "repeat" or "repeat-x" or "repeat-y" or "no-repeat";

    /// <summary>
    /// Sets the transformation matrix of the pattern (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvaspattern-settransform):
    /// a matrix with a non-finite component is ignored.
    /// </summary>
    /// <exception cref="TypeErrorException">The members of the dictionary are inconsistent.</exception>
    [IdlMember("setTransform", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvaspattern-settransform")]
    public void SetTransform(DomMatrix2DInit? transform = null)
    {
        if (CanvasRenderingContext2DBase.ToMatrix(transform) is { } matrix)
        {
            _transform = matrix;
        }
    }

    /// <summary>Returns the shader of the pattern: the image from the origin of the user space, transformed by the matrix of the pattern.</summary>
    internal DisplayShader? CreateShader(ImageSampling sampling)
    {
        if (_image.Width <= 0 || _image.Height <= 0)
            return null;

        return new ImageShader(_image, _spreadX, _spreadY, sampling) { LocalTransform = _transform.IsTranslation && _transform.E == 0 && _transform.F == 0 ? null : _transform };
    }
}
