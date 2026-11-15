using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A still JPEG XL image made of layers: images of any size, each at a position on the canvas, combined from the bottom
/// one up with the blend modes of the format, and named. <see cref="Image.Load(string, ImageDecodeOptions?)"/> reads the
/// image the layers make, as a viewer shows it; this type reads and writes the layers themselves.
/// </summary>
/// <remarks>
/// <para>
/// This type is separate from <see cref="Image"/>, whose frames are the full displayed frames of an animation, and from
/// <see cref="ImageCollection"/>, whose entries are never blended: a layer is not shown on its own. Each layer is a frame
/// of the file without duration, blended with the layers below it, which a decoder keeps in a reference slot: the last
/// layer is the image.
/// </para>
/// <para>
/// <see cref="Save(string, JpegXlEncoder?)"/> writes every layer with the settings of a <see cref="JpegXlEncoder"/>
/// (lossless by default, lossy alpha included), and <see cref="Load(string, ImageConfiguration?)"/> reads the layers of any
/// still JPEG XL file whose frames are such layers, whatever wrote it: a file with a single frame has one layer. A layer
/// decoded from a lossy file has the samples a decoder blends: a lossless file gives the layers back exactly.
/// </para>
/// <para>
/// The images of the layers belong to the layered image, which disposes them. An instance supports one operation at a time.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Imaging.JpegXl.Tests/JpegXlLayeredImageTests.cs" ref:region="JpegXlLayeredImage_Usage" ref:dedent="true">
/// using (var document = new JpegXlLayeredImage(64, 48))
/// {
///     // The bottom layer covers the canvas; a smaller layer is drawn over it at an offset, weighted by its alpha
///     using var background = new Image&lt;Rgba32&gt;(64, 48, new Rgba32(255, 255, 255, 255));
///     using var badge = new Image&lt;Rgba32&gt;(16, 16, new Rgba32(200, 0, 0, 255));
///     document.AddLayer(background, name: "Background");
///     document.AddLayer(badge, new Point(40, 4), JpegXlBlendMode.Blend, "Badge");
///     document.Save(path);
/// }
///
/// // A viewer shows the image the layers make...
/// using (var image = Image.Load&lt;Rgba32&gt;(path))
/// {
///     Assert.Equal(new Rgba32(200, 0, 0, 255), image.Frames[0][45, 10]);
///     Assert.Equal(new Rgba32(255, 255, 255, 255), image.Frames[0][10, 10]);
/// }
///
/// // ...and the layers are read back, with their positions, blend modes and names
/// using (var document = JpegXlLayeredImage.Load(path))
/// {
///     Assert.Equal(2, document.Layers.Count);
///     Assert.Equal((new Point(40, 4), JpegXlBlendMode.Blend, "Badge"), (document.Layers[1].Offset, document.Layers[1].BlendMode, document.Layers[1].Name));
///     Assert.Equal(new Size(16, 16), document.Layers[1].Image.Size);
/// }
/// </code>
/// </example>
public sealed class JpegXlLayeredImage : IDisposable
{
    private readonly List<JpegXlLayer> _layers = [];
    private bool _disposed;

    /// <summary>Initializes an image without layers, of a canvas size.</summary>
    /// <param name="width">The width of the canvas.</param>
    /// <param name="height">The height of the canvas.</param>
    /// <param name="configuration">The configuration of the layers and of the operations, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive or is larger than JPEG XL allows (2^30).</exception>
    public JpegXlLayeredImage(int width, int height, ImageConfiguration? configuration = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, JpegXlEncoderCodec.MaxDimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, JpegXlEncoderCodec.MaxDimension);
        Size = new Size(width, height);
        Configuration = configuration ?? ImageConfiguration.Default;
        Layers = _layers.AsReadOnly();
    }

    /// <summary>Gets the size of the canvas: the size of the image the layers make.</summary>
    public Size Size { get; }

    /// <summary>Gets the configuration of the layers and of the operations.</summary>
    public ImageConfiguration Configuration { get; }

    /// <summary>
    /// Gets the metadata of the image: the color profile, CICP description or transfer function that describe the samples
    /// of every layer, the orientation, and the EXIF and XMP profiles.
    /// </summary>
    /// <remarks>
    /// The metadata of the images of the layers are not written. A layer whose image has another color profile, CICP
    /// description or transfer function than these is rejected by <c>Save</c>: its samples would be read in a color space
    /// they are not in.
    /// </remarks>
    public ImageMetadata Metadata { get; } = new();

    /// <summary>Gets the layers, from the bottom one up.</summary>
    public IReadOnlyList<JpegXlLayer> Layers { get; }

    /// <summary>Adds a copy of an image as the top layer.</summary>
    /// <param name="image">The pixels of the layer: a still image of one frame, of any size. It is copied: the caller keeps and disposes it.</param>
    /// <param name="offset">The position of the top-left corner of the layer on the canvas; it may be negative, and the layer may extend beyond the canvas.</param>
    /// <param name="blendMode">How the layer is combined with the layers below it.</param>
    /// <param name="name">The name of the layer, or <see langword="null"/>.</param>
    /// <returns>The layer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="image"/> has several frames or a poster frame, or <paramref name="name"/> is longer than <see cref="JpegXlLayer.MaxNameLength"/> bytes in UTF-8.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="blendMode"/> is not valid, or the image is larger than JPEG XL allows (2^30).</exception>
    /// <exception cref="ObjectDisposedException">The layered image or <paramref name="image"/> is disposed.</exception>
    public JpegXlLayer AddLayer(Image image, Point offset = default, JpegXlBlendMode blendMode = JpegXlBlendMode.Replace, string? name = null)
        => InsertLayer(_layers.Count, image, offset, blendMode, name);

    /// <summary>Inserts a copy of an image as a layer.</summary>
    /// <param name="index">The index of the new layer, 0 being the bottom one.</param>
    /// <param name="image">The pixels of the layer: a still image of one frame, of any size. It is copied: the caller keeps and disposes it.</param>
    /// <param name="offset">The position of the top-left corner of the layer on the canvas; it may be negative, and the layer may extend beyond the canvas.</param>
    /// <param name="blendMode">How the layer is combined with the layers below it.</param>
    /// <param name="name">The name of the layer, or <see langword="null"/>.</param>
    /// <returns>The layer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="image"/> has several frames or a poster frame, or <paramref name="name"/> is longer than <see cref="JpegXlLayer.MaxNameLength"/> bytes in UTF-8.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/>, <paramref name="offset"/> or <paramref name="blendMode"/> is not valid, or the image is larger than JPEG XL allows (2^30).</exception>
    /// <exception cref="ObjectDisposedException">The layered image or <paramref name="image"/> is disposed.</exception>
    public JpegXlLayer InsertLayer(int index, Image image, Point offset = default, JpegXlBlendMode blendMode = JpegXlBlendMode.Replace, string? name = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _layers.Count);
        JpegXlLayer.ValidateOffset(offset, nameof(offset));
        JpegXlLayer.ValidateBlendMode(blendMode, nameof(blendMode));
        JpegXlLayer.ValidateName(name, nameof(name));
        if (image.Frames.Count != 1 || image.PosterFrame is not null)
            throw new ArgumentException("A layer is a still image of one frame, without poster frame.", nameof(image));

        if (image.Width > JpegXlEncoderCodec.MaxDimension || image.Height > JpegXlEncoderCodec.MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(image), image.Size, "A JPEG XL layer has at most 2^30 pixels on a side.");

        var copy = image.Clone();
        var layer = new JpegXlLayer(copy, offset, blendMode, name);
        _layers.Insert(index, layer);
        return layer;
    }

    /// <summary>Removes a layer and disposes its image.</summary>
    /// <param name="index">The index of the layer, 0 being the bottom one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the index of a layer.</exception>
    /// <exception cref="ObjectDisposedException">The layered image is disposed.</exception>
    public void RemoveLayerAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _layers.Count);
        var layer = _layers[index];
        _layers.RemoveAt(index);
        layer.Image.Dispose();
    }

    /// <summary>Reads the layers of a still JPEG XL file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="configuration">The configuration of the layers and of the decoding, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The layered image, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The input is not a JPEG XL file, is an animation, or blends its frames in a way that is not a list of layers (with
    /// another reference frame than the layers below, or the alpha channel otherwise than the colors).
    /// </exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A limit of the configuration is exceeded: each layer counts as a frame of its own size, charged before it is decoded.</exception>
    public static JpegXlLayeredImage Load(string path, ImageConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return JpegXlLayeredIO.Load(ImageIO.OpenFile(path, asynchronous: false), ownsStream: true, configuration ?? ImageConfiguration.Default);
    }

    /// <summary>Reads the layers of a still JPEG XL file from a stream, from its current position. The stream is left open.</summary>
    /// <param name="stream">The readable input.</param>
    /// <param name="configuration">The configuration of the layers and of the decoding, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The layered image, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The input is not a JPEG XL file, is an animation, or blends its frames in a way that is not a list of layers (with
    /// another reference frame than the layers below, or the alpha channel otherwise than the colors).
    /// </exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A limit of the configuration is exceeded: each layer counts as a frame of its own size, charged before it is decoded.</exception>
    public static JpegXlLayeredImage Load(Stream stream, ImageConfiguration? configuration = null)
    {
        Image.ValidateReadableStream(stream);
        return JpegXlLayeredIO.Load(stream, ownsStream: false, configuration ?? ImageConfiguration.Default);
    }

    /// <summary>Reads the layers of a still JPEG XL file in memory.</summary>
    /// <param name="data">The file.</param>
    /// <param name="configuration">The configuration of the layers and of the decoding, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>The layered image, which the caller disposes.</returns>
    /// <exception cref="UnknownImageFormatException">The format of the input is not recognized.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The input is not a JPEG XL file, is an animation, or blends its frames in a way that is not a list of layers (with
    /// another reference frame than the layers below, or the alpha channel otherwise than the colors).
    /// </exception>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="ImageResourceLimitException">A limit of the configuration is exceeded: each layer counts as a frame of its own size, charged before it is decoded.</exception>
    public static JpegXlLayeredImage Load(ReadOnlySpan<byte> data, ImageConfiguration? configuration = null)
        => JpegXlLayeredIO.Load(data, configuration ?? ImageConfiguration.Default);

    /// <summary>Writes the image to a file, published atomically: each layer is a frame of the file.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="encoder">The settings of the frames, or <see langword="null"/> for the default ones (lossless). <see cref="JpegXlEncoder.DurationRounding"/> and <see cref="JpegXlEncoder.OptimizeFrames"/> do not apply to a still image.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The image has no layer, its layers do not have the same pixel format, or the image of a layer has several frames or a poster frame.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// A layer has floating-point samples without <see cref="ImageEncoder.FloatingPointConversion"/>, CMYK samples, or other
    /// color metadata than <see cref="Metadata"/>; or the metadata cannot be stored (see <see cref="ImageEncoder.MetadataHandling"/>).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The layered image is disposed.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public void Save(string path, JpegXlEncoder? encoder = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(path);
        JpegXlLayeredIO.Save(this, path, encoder ?? new JpegXlEncoder());
    }

    /// <summary>Writes the image to a stream, from its current position: each layer is a frame of the file. The stream is left open.</summary>
    /// <param name="stream">The writable destination; it does not need to be seekable.</param>
    /// <param name="encoder">The settings of the frames, or <see langword="null"/> for the default ones (lossless). <see cref="JpegXlEncoder.DurationRounding"/> and <see cref="JpegXlEncoder.OptimizeFrames"/> do not apply to a still image.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not writable.</exception>
    /// <exception cref="InvalidOperationException">The image has no layer, its layers do not have the same pixel format, or the image of a layer has several frames or a poster frame.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// A layer has floating-point samples without <see cref="ImageEncoder.FloatingPointConversion"/>, CMYK samples, or other
    /// color metadata than <see cref="Metadata"/>; or the metadata cannot be stored (see <see cref="ImageEncoder.MetadataHandling"/>).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The layered image is disposed.</exception>
    public void Save(Stream stream, JpegXlEncoder? encoder = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream is not writable.", nameof(stream));

        JpegXlLayeredIO.Save(this, stream, encoder ?? new JpegXlEncoder());
    }

    /// <summary>Disposes the images of the layers.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var layer in _layers)
        {
            layer.Image.Dispose();
        }

        _layers.Clear();
    }

    /// <summary>Adds a layer whose image the layered image takes over (decoding).</summary>
    internal void AdoptLayer(Image image, Point offset, JpegXlBlendMode blendMode, string? name) => _layers.Add(new JpegXlLayer(image, offset, blendMode, name));
}
