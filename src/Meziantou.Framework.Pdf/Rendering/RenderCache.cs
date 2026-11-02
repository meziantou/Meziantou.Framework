using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;
using Meziantou.Framework.Pdf.Rendering.Functions;
using Meziantou.Framework.Pdf.Rendering.Graphics;
using Meziantou.Framework.Pdf.Rendering.Shadings;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>
/// What the rendering of the pages of a document reads once and uses many times: the color spaces, functions, fonts,
/// images, glyph procedures and meshes of the document, kept from one page to the next, and the budget of the work of
/// the page being rendered.
/// </summary>
/// <remarks>
/// The objects are found by the objects of the document they are made from, and by the color spaces of the resources
/// for those that depend on them (<c>/DefaultRGB</c>, a named color space). The caches are emptied between two pages
/// when they hold more than <see cref="MaxCachedBytes"/>; what a page creates for itself (inline images, the images of
/// its soft masks) is released when the page is rendered.
/// </remarks>
internal sealed class RenderCache(PdfRenderOptions options) : IDisposable
{
    /// <summary>The largest size of the objects the caches keep from one page to the next, measured roughly.</summary>
    public const long MaxCachedBytes = 256L * 1024 * 1024;

    // The temporary buffers of the font code
    private const long MaxFontAllocationBytes = 64L * 1024 * 1024;

    // A glyph procedure is kept parsed up to this length
    private const int MaxParsedContentLength = 64 * 1024;

    // The images of the soft masks a page renders once for the drawings they mask, at the size of the device
    private const int MaxSoftMaskImages = 2;
    private const long MaxSoftMaskPixels = 16L * 1024 * 1024;

    // A rough cost of the objects whose size is not measured: a color space, a function, a font
    private const long ObjectCost = 1024;

    private readonly Dictionary<(PdfObject Value, PdfDictionary? ColorSpaces), RenderColorSpace> _colorSpaces = new(ReferenceKeyComparer.Instance);
    private readonly Dictionary<PdfObject, RenderFunction> _functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PdfObject Value, PdfDictionary? ColorSpaces), object?> _objects = new(ReferenceKeyComparer.Instance);
    private readonly Dictionary<PdfObject, Rgba32[]> _shadingColors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfDictionary, bool> _blendModes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, ParsedContent?> _contents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, Mesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SoftMaskKey, SoftMaskEntry> _softMasks = [];
    private readonly List<IDisposable> _cachedDisposables = [];
    private readonly List<IDisposable> _pageDisposables = [];
    private int _creating;
    private int _softMaskImages;
    private long _operations;

    public PdfRenderOptions Options { get; } = options;

    public FontEnvironment FontEnvironment { get; } = new PooledFontEnvironment(FontResourceLimits.Default, MaxFontAllocationBytes);

    /// <summary>Gets the size of the objects the caches keep, measured roughly.</summary>
    public long CachedBytes { get; private set; }

    /// <summary>Starts the rendering of a page: its work is counted from 0, and the caches are emptied when they hold too much.</summary>
    public void BeginPage()
    {
        _operations = 0;
        if (CachedBytes > MaxCachedBytes)
        {
            ClearObjects();
        }
    }

    /// <summary>Ends the rendering of a page: what it created for itself is released.</summary>
    public void EndPage()
    {
        foreach (var entry in _softMasks.Values)
        {
            entry.Image?.Dispose();
        }

        _softMasks.Clear();
        _softMaskImages = 0;
        DisposeAll(_pageDisposables);
    }

    /// <summary>Gets a color space read from an array, which the color spaces of the resources it is read with may change (8.6.5.6).</summary>
    public RenderColorSpace GetColorSpace(PdfObject key, PdfDictionary? resources, Func<RenderColorSpace> create)
    {
        var cacheKey = (key, GetColorSpaces(resources));
        if (!_colorSpaces.TryGetValue(cacheKey, out var value))
        {
            value = create();
            _colorSpaces[cacheKey] = value;
            CachedBytes += ObjectCost;
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
            CachedBytes += ObjectCost;
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
            CachedBytes += colors.Length * 4L;
        }

        return colors;
    }

    /// <summary>
    /// Gets the triangles of a mesh shading, reading its stream once: a shading pattern is rasterized for each drawing
    /// it paints, each glyph of a text. The color space of a shading does not depend on resources.
    /// </summary>
    public Mesh GetMesh(PdfStream stream, Func<Mesh> create)
    {
        if (!_meshes.TryGetValue(stream, out var mesh))
        {
            mesh = create();
            _meshes[stream] = mesh;
            CachedBytes += ObjectCost + (mesh.Triangles.Count * 256L);
        }

        return mesh;
    }

    /// <summary>Gets an object made from a PDF object (a font), creating it once; a failure is cached as <see langword="null"/>.</summary>
    public T? GetOrCreate<T>(PdfObject key, Func<T?> create)
        where T : class => GetOrCreate(key, resources: null, create);

    /// <summary>
    /// Gets an object made from a PDF object and resources (an image, whose color space the color spaces of the
    /// resources may change), creating it once per dictionary of color spaces; a failure is cached as <see langword="null"/>.
    /// </summary>
    public T? GetOrCreate<T>(PdfObject key, PdfDictionary? resources, Func<T?> create)
        where T : class
    {
        var cacheKey = (key, GetColorSpaces(resources));
        if (_objects.TryGetValue(cacheKey, out var value))
            return value as T;

        T? result;
        _creating++;
        try
        {
            result = create();
        }
        catch (Exception ex) when (IsContentError(ex))
        {
            result = null;
        }
        finally
        {
            _creating--;
        }

        _objects[cacheKey] = result;
        CachedBytes += ObjectCost;
        return result;
    }

    /// <summary>
    /// Gets the operations of a content stream that runs many times (a glyph procedure of a Type 3 font), parsed once;
    /// <see langword="null"/> for a stream too long to keep, which is decoded and parsed each time it runs.
    /// </summary>
    public ParsedContent? GetParsedContent(PdfStream stream)
    {
        if (_contents.TryGetValue(stream, out var content))
            return content;

        var data = stream.GetDecodedData();
        content = data.Length > MaxParsedContentLength ? null : ParsedContent.Parse(data);
        _contents[stream] = content;
        CachedBytes += ObjectCost + (content is null ? 0 : data.Length * 8L);
        return content;
    }

    /// <summary>
    /// Gets the image of a soft mask rendered over the device, when the mask masks several drawings of the page (the
    /// glyphs of a text): the first drawing renders the mask itself (<see langword="null"/>), the next ones use the
    /// image, which <paramref name="render"/> draws once.
    /// </summary>
    public Image<Rgba32>? GetSoftMaskImage(SoftMaskKey key, DrawingAction render)
    {
        if (!_softMasks.TryGetValue(key, out var entry))
        {
            _softMasks[key] = new SoftMaskEntry();
            return null;
        }

        if (entry.Image is not null || entry.IsUncached)
            return entry.Image;

        entry.IsUncached = true;
        if (_softMaskImages >= MaxSoftMaskImages || (long)key.Width * key.Height > MaxSoftMaskPixels)
            return null;

        Charge(((long)key.Width * key.Height / 1024) + 1);
        var image = new Image<Rgba32>(key.Width, key.Height, Options.Configuration);
        try
        {
            image.Draw(render);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        entry.Image = image;
        entry.IsUncached = false;
        _softMaskImages++;
        return image;
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

    /// <summary>
    /// Keeps an object (an image) until it is not used anymore, then disposes of it: with the caches when it is created
    /// for one of them (<see cref="GetOrCreate{T}(PdfObject, PdfDictionary?, Func{T})"/>), else when the page is rendered.
    /// </summary>
    public T Track<T>(T value)
        where T : IDisposable
    {
        if (_creating > 0)
        {
            _cachedDisposables.Add(value);
            if (value is Image image)
            {
                CachedBytes += (long)image.Width * image.Height * 4;
            }
        }
        else
        {
            _pageDisposables.Add(value);
        }

        return value;
    }

    public void Dispose()
    {
        EndPage();
        ClearObjects();
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

    /// <summary>Gets what a color space read with resources depends on: their dictionary of color spaces, with its named and default color spaces.</summary>
    private static PdfDictionary? GetColorSpaces(PdfDictionary? resources) => resources?.Get<PdfDictionary>(PdfNames.ColorSpace);

    private static void DisposeAll(List<IDisposable> disposables)
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        disposables.Clear();
    }

    private void ClearObjects()
    {
        _colorSpaces.Clear();
        _functions.Clear();
        _objects.Clear();
        _shadingColors.Clear();
        _blendModes.Clear();
        _contents.Clear();
        _meshes.Clear();
        DisposeAll(_cachedDisposables);
        CachedBytes = 0;
    }

    /// <summary>The key of the image of a soft mask: the mask, the matrix it was set with, the device and the nesting it is rendered at.</summary>
    internal readonly struct SoftMaskKey(PdfDictionary mask, PdfMatrix matrix, int width, int height, int depth) : IEquatable<SoftMaskKey>
    {
        public PdfDictionary Mask { get; } = mask;

        public PdfMatrix Matrix { get; } = matrix;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public int Depth { get; } = depth;

        public bool Equals(SoftMaskKey other) => ReferenceEquals(Mask, other.Mask) && Matrix.Equals(other.Matrix) && Width == other.Width && Height == other.Height && Depth == other.Depth;

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is SoftMaskKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Mask), Matrix, Width, Height, Depth);
    }

    private sealed class SoftMaskEntry
    {
        public Image<Rgba32>? Image { get; set; }

        public bool IsUncached { get; set; }
    }

    /// <summary>Compares the keys of the caches by the identity of their objects.</summary>
    private sealed class ReferenceKeyComparer : IEqualityComparer<(PdfObject Value, PdfDictionary? ColorSpaces)>
    {
        public static ReferenceKeyComparer Instance { get; } = new();

        public bool Equals((PdfObject Value, PdfDictionary? ColorSpaces) x, (PdfObject Value, PdfDictionary? ColorSpaces) y) => ReferenceEquals(x.Value, y.Value) && ReferenceEquals(x.ColorSpaces, y.ColorSpaces);

        public int GetHashCode((PdfObject Value, PdfDictionary? ColorSpaces) obj) => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Value), obj.ColorSpaces is null ? 0 : RuntimeHelpers.GetHashCode(obj.ColorSpaces));
    }
}
