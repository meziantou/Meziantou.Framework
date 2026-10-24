using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>
/// What the rendering of a page reads once and uses many times: the color spaces, functions, fonts and images of the
/// document, and the budget of the work of the page.
/// </summary>
internal sealed class RenderCache(PdfRenderOptions options) : IDisposable
{
    // The temporary buffers of the font code
    private const long MaxFontAllocationBytes = 64L * 1024 * 1024;

    private readonly Dictionary<PdfObject, RenderColorSpace> _colorSpaces = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, RenderFunction> _functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, object?> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, Rgba32[]> _shadingColors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfDictionary, bool> _blendModes = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> _disposables = [];
    private long _operations;

    public PdfRenderOptions Options { get; } = options;

    public FontEnvironment FontEnvironment { get; } = new PooledFontEnvironment(FontResourceLimits.Default, MaxFontAllocationBytes);

    public RenderColorSpace GetColorSpace(PdfObject key, Func<RenderColorSpace> create)
    {
        if (!_colorSpaces.TryGetValue(key, out var value))
        {
            value = create();
            _colorSpaces[key] = value;
        }

        return value;
    }

    public RenderFunction GetFunction(PdfObject value)
    {
        var key = PdfReference.ResolveIfReference(value);
        if (!_functions.TryGetValue(key, out var function))
        {
            function = RenderFunction.Read(key);
            _functions[key] = function;
        }

        return function;
    }

    /// <summary>Gets the colors an axial or radial shading takes along its axis, computing them once: a shading is often painted many times.</summary>
    public Rgba32[] GetShadingColors(PdfDictionary shading, Func<Rgba32[]> create)
    {
        if (!_shadingColors.TryGetValue(shading, out var colors))
        {
            colors = create();
            _shadingColors[shading] = colors;
        }

        return colors;
    }

    /// <summary>Gets an object made from a PDF object (a font, an image), creating it once; a failure is cached as <see langword="null"/>.</summary>
    public T? GetOrCreate<T>(PdfObject key, Func<T?> create)
        where T : class
    {
        if (_objects.TryGetValue(key, out var value))
            return value as T;

        T? result;
        try
        {
            result = create();
        }
        catch (Exception ex) when (IsContentError(ex))
        {
            result = null;
        }

        _objects[key] = result;
        return result;
    }

    /// <summary>
    /// Gets a value indicating whether the graphics states of resources, or of the forms they paint, set a blend mode
    /// other than Normal: an isolated group without them composites as its content does (11.4.8).
    /// </summary>
    public bool UsesBlendModes(PdfDictionary? resources) => UsesBlendModes(resources, depth: 0);

    private bool UsesBlendModes(PdfDictionary? resources, int depth)
    {
        if (resources is null)
            return false;

        if (_blendModes.TryGetValue(resources, out var value))
            return value;

        // Assumed while the resources are visited, so that a cycle of forms ends
        _blendModes[resources] = depth > 8;
        if (depth > 8)
            return true;

        var result = false;
        if (resources.Get<PdfDictionary>(PdfNames.ExtGState) is { } states)
        {
            foreach (var (_, state) in states)
            {
                if (PdfReference.ResolveIfReference(state) is PdfDictionary dictionary && dictionary.GetResolved(PdfName.Create("BM")) switch
                {
                    null => false,
                    PdfName { Value: "Normal" or "Compatible" } => false,
                    PdfArray { Count: > 0 } modes => modes.GetResolved(0) is not PdfName { Value: "Normal" or "Compatible" },
                    _ => true,
                })
                {
                    result = true;
                    break;
                }
            }
        }

        if (!result && resources.Get<PdfDictionary>(PdfNames.XObject) is { } xobjects)
        {
            foreach (var (_, xobject) in xobjects)
            {
                if (PdfReference.ResolveIfReference(xobject) is PdfStream { } form && form.Dictionary.GetName(PdfNames.Subtype) == PdfNames.Form
                    && UsesBlendModes(form.Dictionary.Get<PdfDictionary>(PdfNames.Resources), depth + 1))
                {
                    result = true;
                    break;
                }
            }
        }

        _blendModes[resources] = result;
        return result;
    }

    /// <summary>
    /// Indicates whether an exception is an error of the content of the document, which the rendering skips: a damaged
    /// object, or an image or a font beyond the limits of the libraries that decode it.
    /// </summary>
    public static bool IsContentError(Exception exception) => exception is InvalidPdfContentException or UnsupportedPdfFeatureException or InvalidFontException or UnsupportedFontFeatureException or FontResourceLimitException or Imaging.InvalidImageContentException or Imaging.UnsupportedImageFeatureException or Imaging.ImageResourceLimitException;

    /// <summary>Keeps an object (an image) until the page is rendered, then disposes of it.</summary>
    public T Track<T>(T value)
        where T : IDisposable
    {
        _disposables.Add(value);
        return value;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        _disposables.Clear();
    }

    /// <summary>Creates the working storage of the outlines of a font, disposed of with the cache.</summary>
    public GlyphScratch CreateGlyphScratch(FontEnvironment environment, string name) => Track(new GlyphScratch(environment.CreateAllocator(name)));

    /// <summary>Charges the work of the page.</summary>
    /// <exception cref="PdfResourceLimitException">The page does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public void Charge(long count)
    {
        _operations += Math.Max(count, 1);
        if (_operations > Options.MaxOperations)
            throw new PdfResourceLimitException($"The page does more than {Options.MaxOperations} operations");
    }
}
