using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Guards the shape of the public API beyond what the compiler checks: closed extensibility, immutability, dependencies.</summary>
public sealed class ApiSurfaceTests
{
    private static readonly Assembly LibraryAssembly = typeof(Image).Assembly;

    private static readonly Type[] ImmutableSettingTypes =
    [
        typeof(ImageConfiguration),
        typeof(ImageResourceLimits),
        typeof(ImageDecodeOptions),
        typeof(ImageIdentifyOptions),
        typeof(ImageReaderOptions),
        typeof(ImageWriterOptions),
        typeof(PixelConversionOptions),
        typeof(ResizeOptions),
        typeof(TransformOptions),
        typeof(ConvolutionOptions),
        typeof(ConvolutionKernel),
        typeof(AutoCropOptions),
        typeof(CropSuggestionOptions),
        typeof(SeamCarvingOptions),
        typeof(InpaintingOptions),
        typeof(HoughLineOptions),
        typeof(CornerDetectionOptions),
        typeof(HomographyRansacOptions),
        typeof(SkewEstimationOptions),
        typeof(RadialBlurOptions),
        typeof(ImageSimilarityOptions),
        typeof(DifferenceMapOptions),
        typeof(IccColorTransformOptions),
        typeof(CmykConversionOptions),
        typeof(OpenExrDecodingOptions),
        typeof(PngEncoder),
        typeof(GifEncoder),
        typeof(JpegEncoder),
        typeof(AvifEncoder),
        typeof(HeicEncoder),
        typeof(Pen),
        typeof(SolidBrush),
        typeof(LinearGradientBrush),
        typeof(RadialGradientBrush),
        typeof(VectorPath),
        typeof(FontLoadOptions),
        typeof(TextStyle),
        typeof(TextLayoutOptions),
        typeof(TextPathOptions),
    ];

    [Fact]
    public void PublicTypesAreInDocumentedNamespaces()
    {
        string[] allowed = ["Meziantou.Framework.Imaging", "Meziantou.Framework.Imaging.Metadata", "Meziantou.Framework.Imaging.Formats", "Meziantou.Framework.Imaging.Drawing", "Meziantou.Framework.Imaging.Typography"];
        Assert.All(LibraryAssembly.GetExportedTypes(), type => Assert.Contains(type.Namespace, allowed));
    }

    [Theory]
    [InlineData(typeof(Image))]
    [InlineData(typeof(ImageFrame))]
    [InlineData(typeof(ImageFrameCollection))]
    [InlineData(typeof(ImageEncoder))]
    [InlineData(typeof(AutoCropAnalysis))]
    [InlineData(typeof(Brush))]
    [InlineData(typeof(GradientBrush))]
    public void ExtensibilityIsClosed(Type type)
    {
        // The base types of the model cannot be derived outside the library: the extension points are their own types
        // (CustomImageCodec, CustomImageEncoder, the pixel operations, ICustomPixel and ImageBufferAllocator), and there
        // is no custom frame contract
        var accessibleConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly);
        Assert.Empty(accessibleConstructors);
    }

    [Fact]
    public void ExtensionPointsHaveTheirPublicShape()
    {
        // The custom format is out of the range of the built-in ones, which are appended after OpenExr
        Assert.Equal(1000, (int)ImageFormat.Custom);

        foreach (var type in new[] { typeof(CustomImageCodec), typeof(CustomImageEncoder) })
        {
            Assert.True(type.IsPublic && type.IsAbstract);
            var constructor = Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            Assert.True(constructor.IsFamily);
            Assert.Equal([typeof(ImageFormatInfo)], constructor.GetParameters().Select(parameter => parameter.ParameterType));
        }

        Assert.Equal("Meziantou.Framework.Imaging.Formats", typeof(CustomImageEncoder).Namespace);
        Assert.True(typeof(CustomImageDecodeContext).IsSealed);
        Assert.Empty(typeof(CustomImageDecodeContext).GetConstructors());
        Assert.True(typeof(ImageBufferPool).IsAbstract && typeof(ImageBufferPool).IsSealed);
        Assert.True(typeof(PixelRowSource<>).IsByRefLike);
        Assert.True(typeof(IPixelRowOperation<>).IsInterface && typeof(IPixelNeighborhoodOperation<>).IsInterface);
    }

    [Theory]
    [InlineData(typeof(Image<>))]
    [InlineData(typeof(ImageFrame<>))]
    [InlineData(typeof(ImageFrameCollection<>))]
    [InlineData(typeof(ImageReader<>))]
    [InlineData(typeof(ImageWriter<>))]
    [InlineData(typeof(ImageInfo))]
    [InlineData(typeof(AutoCropAnalysis<>))]
    [InlineData(typeof(ImageMetadata))]
    [InlineData(typeof(FrameMetadata))]
    [InlineData(typeof(AnimationMetadata))]
    [InlineData(typeof(MetadataBlob))]
    [InlineData(typeof(IccProfile))]
    [InlineData(typeof(IccColorTransform))]
    [InlineData(typeof(CicpColorDescription))]
    [InlineData(typeof(ContentLightLevel))]
    [InlineData(typeof(MasteringDisplayColorVolume))]
    [InlineData(typeof(PixelAspectRatio))]
    [InlineData(typeof(GainMapMetadata))]
    [InlineData(typeof(GainMapChannel))]
    [InlineData(typeof(AuxiliaryImageCollection))]
    [InlineData(typeof(AuxiliaryImage))]
    [InlineData(typeof(VectorPathBuilder))]
    [InlineData(typeof(Font))]
    [InlineData(typeof(FontCollection))]
    [InlineData(typeof(TextLayout))]
    [InlineData(typeof(RichText))]
    [InlineData(typeof(RichTextBuilder))]
    [InlineData(typeof(FontStack))]
    public void ConcreteTypesAreSealed(Type type) => Assert.True(type.IsSealed, $"{type} must be sealed");

    [Fact]
    public void ImmutableSettingsAreSealedAndHaveNoPublicSetters()
    {
        foreach (var type in ImmutableSettingTypes)
        {
            Assert.True(type.IsSealed, $"{type} must be sealed");
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var setter = property.GetSetMethod(nonPublic: false);
                if (setter is null)
                    continue;

                var isInitOnly = setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                Assert.True(isInitOnly, $"{type.Name}.{property.Name} must be get-only or init-only");
            }
        }
    }

    [Fact]
    public void AvifAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(13, (int)ImageFormat.Avif);
        Assert.Equal(nameof(ImageFormat.Avif), Enum.GetName((ImageFormat)13));

        // The CICP color description is an immutable value: a public constructor, get-only properties, value equality
        var type = typeof(CicpColorDescription);
        Assert.Equal("Meziantou.Framework.Imaging.Metadata", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsClass);
        Assert.True(typeof(IEquatable<CicpColorDescription>).IsAssignableFrom(type));
        Assert.NotNull(type.GetConstructor([typeof(byte), typeof(byte), typeof(byte), typeof(bool)]));
        Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public));

        // ...attached to the mutable metadata container by a nullable read-write property
        var property = GetDeclaredProperty(typeof(ImageMetadata), nameof(ImageMetadata.Cicp));
        Assert.Equal(type, property.PropertyType);
        Assert.NotNull(property.GetGetMethod());
        Assert.NotNull(property.GetSetMethod());
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
    }

    [Theory]
    [InlineData(typeof(ContentLightLevel), nameof(ImageMetadata.ContentLightLevel))]
    [InlineData(typeof(MasteringDisplayColorVolume), nameof(ImageMetadata.MasteringDisplay))]
    [InlineData(typeof(PixelAspectRatio), nameof(ImageMetadata.PixelAspectRatio))]
    [InlineData(typeof(OpenExrWindows), nameof(ImageMetadata.OpenExrWindows))]
    [InlineData(typeof(RadianceHeader), nameof(ImageMetadata.RadianceHeader))]
    public void HighDynamicRangeAndPixelShapeLabelsHaveTheirPublicShape(Type type, string propertyName)
    {
        // Immutable values with value equality...
        Assert.Equal("Meziantou.Framework.Imaging.Metadata", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsClass);
        Assert.True(typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type));
        Assert.Single(type.GetConstructors());
        Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public));

        // ...attached to the mutable metadata container by a nullable read-write property
        var property = GetDeclaredProperty(typeof(ImageMetadata), propertyName);
        Assert.Equal(type, property.PropertyType);
        Assert.NotNull(property.GetGetMethod());
        Assert.NotNull(property.GetSetMethod());
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
    }

    [Fact]
    public void HeicAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(15, (int)ImageFormat.Heic);
        Assert.Equal(nameof(ImageFormat.Heic), Enum.GetName((ImageFormat)15));

        // The encoder is an immutable settings object with the settings of the AVIF one: the two formats share a container
        var type = typeof(HeicEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        var settings = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                nameof(HeicEncoder.AllowBitDepthReduction), nameof(HeicEncoder.BitDepth), nameof(HeicEncoder.ChromaSubsampling), nameof(HeicEncoder.ClearTransparentColors), nameof(HeicEncoder.Compression),
                nameof(HeicEncoder.DurationRounding), nameof(HeicEncoder.Effort), nameof(HeicEncoder.Format), nameof(HeicEncoder.GridTileSize), nameof(HeicEncoder.KeyFrameInterval), nameof(HeicEncoder.OptimizeQuantization), nameof(HeicEncoder.Quality),
            ],
            settings);

        // The shared settings exclude codec-specific options and container features that only AVIF writes so far
        string[] avifOnly = [nameof(AvifEncoder.AutoTiling), nameof(AvifEncoder.CleanAperture), nameof(AvifEncoder.GainMap), nameof(AvifEncoder.PremultiplyAlpha)];
        string[] heicOnly = [nameof(HeicEncoder.OptimizeQuantization)];
        Assert.Equal(typeof(AvifEncoder).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Except(avifOnly).Order(StringComparer.Ordinal), settings.Except(heicOnly));
        Assert.Equal(typeof(bool), GetDeclaredProperty(type, nameof(HeicEncoder.OptimizeQuantization)).PropertyType);
        Assert.Equal(typeof(HeicCompression), GetDeclaredProperty(type, nameof(HeicEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(HeicChromaSubsampling), GetDeclaredProperty(type, nameof(HeicEncoder.ChromaSubsampling)).PropertyType);
        Assert.Equal(Enum.GetNames<AvifCompression>(), Enum.GetNames<HeicCompression>());
        Assert.Equal(Enum.GetNames<AvifChromaSubsampling>(), Enum.GetNames<HeicChromaSubsampling>());
    }

    [Fact]
    public void JpegXlAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(14, (int)ImageFormat.JpegXl);
        Assert.Equal(nameof(ImageFormat.JpegXl), Enum.GetName((ImageFormat)14));

        // The encoder is an immutable settings object like the others: sealed, a parameterless constructor, init-only settings
        var type = typeof(JpegXlEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(JpegXlEncoder.Compression), nameof(JpegXlEncoder.DurationRounding), nameof(JpegXlEncoder.Effort), nameof(JpegXlEncoder.Format), nameof(JpegXlEncoder.OptimizeFrames), nameof(JpegXlEncoder.Progressive), nameof(JpegXlEncoder.Quality)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(bool), GetDeclaredProperty(type, nameof(JpegXlEncoder.OptimizeFrames)).PropertyType);
        Assert.Equal(typeof(int), GetDeclaredProperty(type, nameof(JpegXlEncoder.Effort)).PropertyType);
        Assert.Equal(typeof(FrameDurationRounding), GetDeclaredProperty(type, nameof(JpegXlEncoder.DurationRounding)).PropertyType);
        Assert.Equal(typeof(JpegXlCompression), GetDeclaredProperty(type, nameof(JpegXlEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(int), GetDeclaredProperty(type, nameof(JpegXlEncoder.Quality)).PropertyType);
        Assert.Equal(typeof(bool), GetDeclaredProperty(type, nameof(JpegXlEncoder.Progressive)).PropertyType);

        // The lossless coding is the default value of the enumeration, like the other formats that have two codings
        Assert.Equal(0, (int)JpegXlCompression.Lossless);
        Assert.Equal(1, (int)JpegXlCompression.Lossy);
    }

    [Fact]
    public void JpegXlProgressiveReadingAndTranscodingHaveTheirPublicShape()
    {
        // A description the reader creates: get-only, no public constructor
        var refinement = typeof(FrameRefinement);
        Assert.Equal("Meziantou.Framework.Imaging", refinement.Namespace);
        Assert.True(refinement.IsPublic && refinement.IsSealed);
        Assert.Empty(refinement.GetConstructors());
        Assert.Equal(
            [nameof(FrameRefinement.CompletedPasses), nameof(FrameRefinement.Downsampling), nameof(FrameRefinement.IsFinal)],
            refinement.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(refinement.GetProperties(), property => property.GetSetMethod() is null);
        Assert.Equal(typeof(FrameRefinement), typeof(ImageReader<Rgb24>).GetMethod(nameof(ImageReader<>.ReadFrameRefinementInto))!.ReturnType);
        Assert.Equal(typeof(ValueTask<FrameRefinement?>), typeof(ImageReader<Rgb24>).GetMethod(nameof(ImageReader<>.ReadFrameRefinementIntoAsync))!.ReturnType);

        // The conversions between files are static: they neither take nor give an image
        var transcoder = typeof(JpegXlTranscoder);
        Assert.Equal("Meziantou.Framework.Imaging", transcoder.Namespace);
        Assert.True(transcoder.IsPublic && transcoder.IsAbstract && transcoder.IsSealed);
        Assert.Equal(
            [
                nameof(JpegXlTranscoder.HasJpegReconstructionData), nameof(JpegXlTranscoder.HasJpegReconstructionDataAsync),
                nameof(JpegXlTranscoder.RecompressJpeg), nameof(JpegXlTranscoder.RecompressJpegAsync),
                nameof(JpegXlTranscoder.ReconstructJpeg), nameof(JpegXlTranscoder.ReconstructJpegAsync),
                nameof(JpegXlTranscoder.TryReconstructJpeg), nameof(JpegXlTranscoder.TryReconstructJpegAsync),
            ],
            transcoder.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(method => method.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(transcoder.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly), method => !method.GetParameters().Any(parameter => typeof(Image).IsAssignableFrom(parameter.ParameterType)) && !typeof(Image).IsAssignableFrom(method.ReturnType));

        // Immutable options, like the other ones
        var options = typeof(JpegXlTranscodeOptions);
        Assert.True(options.IsPublic && options.IsSealed);
        Assert.NotNull(options.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(JpegXlTranscodeOptions.Configuration), nameof(JpegXlTranscodeOptions.Effort)],
            options.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void JpegXlLayersHaveTheirPublicShape()
    {
        // A layered image is neither an image nor a collection: it owns its layers, reads and writes them
        var type = typeof(JpegXlLayeredImage);
        Assert.Equal("Meziantou.Framework.Imaging", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && typeof(IDisposable).IsAssignableFrom(type) && !typeof(Image).IsAssignableFrom(type));
        Assert.Equal(
            [nameof(JpegXlLayeredImage.Configuration), nameof(JpegXlLayeredImage.Layers), nameof(JpegXlLayeredImage.Metadata), nameof(JpegXlLayeredImage.Size)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), property => Assert.Null(property.GetSetMethod()));
        Assert.Equal(
            [nameof(JpegXlLayeredImage.AddLayer), nameof(JpegXlLayeredImage.Dispose), nameof(JpegXlLayeredImage.InsertLayer), nameof(JpegXlLayeredImage.Load), nameof(JpegXlLayeredImage.RemoveLayerAt), nameof(JpegXlLayeredImage.Save)],
            type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName).Select(method => method.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(IReadOnlyList<JpegXlLayer>), GetDeclaredProperty(type, nameof(JpegXlLayeredImage.Layers)).PropertyType);

        // A layer is created by its layered image; its image is get-only, its placement can change
        var layer = typeof(JpegXlLayer);
        Assert.True(layer.IsPublic && layer.IsSealed);
        Assert.Empty(layer.GetConstructors());
        Assert.Equal(
            [nameof(JpegXlLayer.BlendMode), nameof(JpegXlLayer.Image), nameof(JpegXlLayer.Name), nameof(JpegXlLayer.Offset)],
            layer.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Null(GetDeclaredProperty(layer, nameof(JpegXlLayer.Image)).GetSetMethod());

        // The blend modes have the values of the format
        Assert.Equal([nameof(JpegXlBlendMode.Replace), nameof(JpegXlBlendMode.Add), nameof(JpegXlBlendMode.Blend), nameof(JpegXlBlendMode.AlphaWeightedAdd), nameof(JpegXlBlendMode.Multiply)], Enum.GetNames<JpegXlBlendMode>());
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<JpegXlBlendMode>().Select(value => (int)value));
    }

    [Fact]
    public void Jpeg2000AdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(16, (int)ImageFormat.Jpeg2000);
        Assert.Equal(nameof(ImageFormat.Jpeg2000), Enum.GetName((ImageFormat)16));

        var type = typeof(Jpeg2000Encoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(Jpeg2000Encoder.BlockCoder), nameof(Jpeg2000Encoder.Compression), nameof(Jpeg2000Encoder.Container), nameof(Jpeg2000Encoder.DecompositionLevels), nameof(Jpeg2000Encoder.Format), nameof(Jpeg2000Encoder.PrecinctSizes), nameof(Jpeg2000Encoder.ProgressionOrder), nameof(Jpeg2000Encoder.Quality), nameof(Jpeg2000Encoder.QualityLayers), nameof(Jpeg2000Encoder.TileHeight), nameof(Jpeg2000Encoder.TilePartDivision), nameof(Jpeg2000Encoder.TileWidth), nameof(Jpeg2000Encoder.WritePacketLengths), nameof(Jpeg2000Encoder.WriteTilePartLengths)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(Jpeg2000Compression), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Compression)).PropertyType);
        Assert.Equal(typeof(Jpeg2000Container), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Container)).PropertyType);
        Assert.Equal(typeof(int), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Quality)).PropertyType);
        Assert.Equal(typeof(IReadOnlyList<Jpeg2000QualityLayer>), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.QualityLayers)).PropertyType);
        Assert.Equal(typeof(Jpeg2000ProgressionOrder), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.ProgressionOrder)).PropertyType);
        Assert.Equal(typeof(Jpeg2000BlockCoder), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.BlockCoder)).PropertyType);
        Assert.Equal(typeof(IReadOnlyList<Size>), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.PrecinctSizes)).PropertyType);
        Assert.Equal(typeof(Jpeg2000TilePartDivision), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.TilePartDivision)).PropertyType);
        Assert.Equal(typeof(bool), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.WriteTilePartLengths)).PropertyType);
        Assert.Equal(typeof(bool), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.WritePacketLengths)).PropertyType);

        // The defaults are the zero values
        Assert.Equal([nameof(Jpeg2000Compression.Lossless), nameof(Jpeg2000Compression.Lossy)], Enum.GetNames<Jpeg2000Compression>());
        Assert.Equal([nameof(Jpeg2000Container.Jp2), nameof(Jpeg2000Container.Codestream)], Enum.GetNames<Jpeg2000Container>());
        Assert.Equal(0, (int)Jpeg2000Compression.Lossless);
        Assert.Equal(0, (int)Jpeg2000Container.Jp2);
        Assert.Equal([nameof(Jpeg2000BlockCoder.Standard), nameof(Jpeg2000BlockCoder.HighThroughput)], Enum.GetNames<Jpeg2000BlockCoder>());
        Assert.Equal(0, (int)Jpeg2000BlockCoder.Standard);
        Assert.Equal([nameof(Jpeg2000TilePartDivision.None), nameof(Jpeg2000TilePartDivision.Resolution), nameof(Jpeg2000TilePartDivision.Layer), nameof(Jpeg2000TilePartDivision.Component)], Enum.GetNames<Jpeg2000TilePartDivision>());
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<Jpeg2000TilePartDivision>().Select(division => (int)division));

        // The progression orders have the values of the COD marker segment (T.800, Table A.16)
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<Jpeg2000ProgressionOrder>().Select(order => (int)order));
        Assert.Equal([nameof(Jpeg2000ProgressionOrder.Lrcp), nameof(Jpeg2000ProgressionOrder.Rlcp), nameof(Jpeg2000ProgressionOrder.Rpcl), nameof(Jpeg2000ProgressionOrder.Pcrl), nameof(Jpeg2000ProgressionOrder.Cprl)], Enum.GetNames<Jpeg2000ProgressionOrder>());
    }

    [Fact]
    public void OpenExrAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(17, (int)ImageFormat.OpenExr);

        var type = typeof(OpenExrEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(OpenExrEncoder.AllowBitDepthReduction), nameof(OpenExrEncoder.Compression), nameof(OpenExrEncoder.DwaCompressionLevel), nameof(OpenExrEncoder.Format), nameof(OpenExrEncoder.LineOrder), nameof(OpenExrEncoder.PixelType), nameof(OpenExrEncoder.Tiling)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(OpenExrCompression), GetDeclaredProperty(type, nameof(OpenExrEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(OpenExrPixelType?), GetDeclaredProperty(type, nameof(OpenExrEncoder.PixelType)).PropertyType);

        // The compressions have the values of the format; the pixel types have no default value but null
        Assert.Equal(
            [nameof(OpenExrCompression.None), nameof(OpenExrCompression.Rle), nameof(OpenExrCompression.Zips), nameof(OpenExrCompression.Zip), nameof(OpenExrCompression.Piz), nameof(OpenExrCompression.Pxr24), nameof(OpenExrCompression.B44), nameof(OpenExrCompression.B44A), nameof(OpenExrCompression.Dwaa), nameof(OpenExrCompression.Dwab)],
            Enum.GetNames<OpenExrCompression>());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], Enum.GetValues<OpenExrCompression>().Select(value => (int)value));
        Assert.Equal([nameof(OpenExrPixelType.Half), nameof(OpenExrPixelType.Float)], Enum.GetNames<OpenExrPixelType>());

        // The line orders and the level modes have the values of the format; the tiling is a sealed class of settings
        Assert.Equal([0, 1, 2], Enum.GetValues<OpenExrLineOrder>().Select(value => (int)value));
        Assert.Equal([nameof(OpenExrLineOrder.IncreasingY), nameof(OpenExrLineOrder.DecreasingY), nameof(OpenExrLineOrder.RandomY)], Enum.GetNames<OpenExrLineOrder>());
        Assert.Equal([nameof(OpenExrLevelMode.OneLevel), nameof(OpenExrLevelMode.MipmapLevels), nameof(OpenExrLevelMode.RipmapLevels)], Enum.GetNames<OpenExrLevelMode>());
        Assert.Equal([nameof(OpenExrLevelRounding.Down), nameof(OpenExrLevelRounding.Up)], Enum.GetNames<OpenExrLevelRounding>());
        Assert.Equal(typeof(OpenExrTiling), GetDeclaredProperty(type, nameof(OpenExrEncoder.Tiling)).PropertyType);
        Assert.True(typeof(OpenExrTiling).IsSealed);
        Assert.Equal(
            [nameof(OpenExrTiling.LevelMode), nameof(OpenExrTiling.LevelRounding), nameof(OpenExrTiling.TileHeight), nameof(OpenExrTiling.TileWidth)],
            typeof(OpenExrTiling).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PsdAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(19, (int)ImageFormat.Psd);

        var type = typeof(PsdEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(PsdEncoder.Compression), nameof(PsdEncoder.Format), nameof(PsdEncoder.LargeDocument)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(PsdCompression), GetDeclaredProperty(type, nameof(PsdEncoder.Compression)).PropertyType);
        Assert.Equal([nameof(PsdCompression.None), nameof(PsdCompression.RunLength)], Enum.GetNames<PsdCompression>());
        Assert.Equal([0, 1], Enum.GetValues<PsdCompression>().Select(value => (int)value));
    }

    [Fact]
    public void DngAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(20, (int)ImageFormat.Dng);

        var type = typeof(DngRawImage);
        Assert.Equal("Meziantou.Framework.Imaging", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && typeof(IDisposable).IsAssignableFrom(type));
        Assert.True(typeof(DngProperties).IsPublic && typeof(DngProperties).IsSealed);
        Assert.Equal(typeof(DngProperties), typeof(ImageMetadata).GetProperty(nameof(ImageMetadata.DngProperties))!.PropertyType);

        var encoder = typeof(DngEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", encoder.Namespace);
        Assert.True(encoder.IsPublic && encoder.IsSealed && encoder.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(encoder.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(DngEncoder.Compression), nameof(DngEncoder.Format), nameof(DngEncoder.TileSize)],
            encoder.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(DngCompression?), GetDeclaredProperty(encoder, nameof(DngEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(int?), GetDeclaredProperty(encoder, nameof(DngEncoder.TileSize)).PropertyType);
        Assert.Equal([nameof(DngCompression.None), nameof(DngCompression.LosslessJpeg), nameof(DngCompression.Deflate)], Enum.GetNames<DngCompression>());
        Assert.Equal([0, 1, 2], Enum.GetValues<DngCompression>().Select(value => (int)value));
    }

    [Fact]
    public void RadianceAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(18, (int)ImageFormat.Radiance);

        var type = typeof(RadianceEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(RadianceEncoder.BackgroundColor), nameof(RadianceEncoder.Compression), nameof(RadianceEncoder.Format)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(RadianceCompression), GetDeclaredProperty(type, nameof(RadianceEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(Rgba64?), GetDeclaredProperty(type, nameof(RadianceEncoder.BackgroundColor)).PropertyType);
        Assert.Equal([nameof(RadianceCompression.None), nameof(RadianceCompression.RunLength)], Enum.GetNames<RadianceCompression>());
        Assert.Equal([0, 1], Enum.GetValues<RadianceCompression>().Select(value => (int)value));
    }

    [Fact]
    public void DdsAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(21, (int)ImageFormat.Dds);
        Assert.Equal(3, (int)ImageCollectionKind.TextureSurfaces);

        var type = typeof(TextureSurface);
        Assert.Equal("Meziantou.Framework.Imaging", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed);
        Assert.Equal([typeof(int), typeof(CubeMapFace?), typeof(int)], Assert.Single(type.GetConstructors()).GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [nameof(TextureSurface.ArrayIndex), nameof(TextureSurface.DepthSlice), nameof(TextureSurface.Face), nameof(TextureSurface.MipLevel)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(type.GetProperties(), property => property.SetMethod is null);
        Assert.Equal(typeof(CubeMapFace?), GetDeclaredProperty(type, nameof(TextureSurface.Face)).PropertyType);
        Assert.Equal(typeof(TextureSurface), GetDeclaredProperty(typeof(ImageCollectionEntry), nameof(ImageCollectionEntry.TextureSurface)).PropertyType);
        Assert.Equal(
            [nameof(CubeMapFace.PositiveX), nameof(CubeMapFace.NegativeX), nameof(CubeMapFace.PositiveY), nameof(CubeMapFace.NegativeY), nameof(CubeMapFace.PositiveZ), nameof(CubeMapFace.NegativeZ)],
            Enum.GetNames<CubeMapFace>());
        Assert.Equal([0, 1, 2, 3, 4, 5], Enum.GetValues<CubeMapFace>().Select(value => (int)value));
    }

    [Fact]
    public void DpxAndCineonAdditionsHaveTheirPublicShape()
    {
        // New formats are appended: the values of the existing members never change
        Assert.Equal(22, (int)ImageFormat.Dpx);
        Assert.Equal(23, (int)ImageFormat.Cineon);

        var type = typeof(DpxEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(DpxEncoder.AllowBitDepthReduction), nameof(DpxEncoder.BitDepth), nameof(DpxEncoder.ByteOrder), nameof(DpxEncoder.Format)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(int?), GetDeclaredProperty(type, nameof(DpxEncoder.BitDepth)).PropertyType);
        Assert.Equal([nameof(DpxByteOrder.BigEndian), nameof(DpxByteOrder.LittleEndian)], Enum.GetNames<DpxByteOrder>());

        var cineon = typeof(CineonEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", cineon.Namespace);
        Assert.True(cineon.IsPublic && cineon.IsSealed && cineon.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(cineon.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(CineonEncoder.AllowBitDepthReduction), nameof(CineonEncoder.BitDepth), nameof(CineonEncoder.Format)],
            cineon.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(int?), GetDeclaredProperty(cineon, nameof(CineonEncoder.BitDepth)).PropertyType);

        // The headers are informational records: sealed, value equality, init-only properties, attached by nullable read-write properties
        foreach (var (header, propertyName) in new[] { (typeof(DpxHeader), nameof(ImageMetadata.DpxHeader)), (typeof(CineonHeader), nameof(ImageMetadata.CineonHeader)) })
        {
            Assert.Equal("Meziantou.Framework.Imaging.Metadata", header.Namespace);
            Assert.True(header.IsPublic && header.IsSealed && header.IsClass);
            Assert.True(typeof(IEquatable<>).MakeGenericType(header).IsAssignableFrom(header));
            Assert.All(header.GetProperties(BindingFlags.Instance | BindingFlags.Public), property => Assert.True(property.GetSetMethod() is null || property.GetSetMethod()!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit))));
            var property = GetDeclaredProperty(typeof(ImageMetadata), propertyName);
            Assert.Equal(header, property.PropertyType);
            Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).ReadState);
        }

        // The codes of the tables of the standard are the values of the enumerations
        Assert.Equal(50, (int)DpxDescriptor.Rgb);
        Assert.Equal(100, (int)DpxDescriptor.CbYCrY);
        Assert.Equal(13, (int)DpxTransferCharacteristic.Adx);
        Assert.Equal(255, (int)DpxTransferCharacteristic.Undefined);
        Assert.Equal(6, (int)DpxColorimetricSpecification.Bt709);
        Assert.Equal([0, 1, 2], Enum.GetValues<DpxPacking>().Select(value => (int)value));
    }

    [Fact]
    public void Ktx2AdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(24, (int)ImageFormat.Ktx2);
        Assert.Equal(24, Enum.GetValues<ImageFormat>().Where(format => format != ImageFormat.Custom).Max(format => (int)format));
        Assert.Equal(3, (int)ImageCollectionKind.TextureSurfaces);

        // Where a texture entry is: an immutable description, created by the decoders or for a texture to save
        var surface = typeof(TextureSurface);
        Assert.True(surface.IsPublic && surface.IsSealed && surface.IsClass);
        Assert.Single(surface.GetConstructors());
        Assert.Equal(
            [nameof(TextureSurface.ArrayIndex), nameof(TextureSurface.DepthSlice), nameof(TextureSurface.Face), nameof(TextureSurface.MipLevel)],
            surface.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(CubeMapFace?), GetDeclaredProperty(surface, nameof(TextureSurface.Face)).PropertyType);
        Assert.Equal(typeof(TextureSurface), GetDeclaredProperty(typeof(ImageCollectionEntry), nameof(ImageCollectionEntry.TextureSurface)).PropertyType);

        // The header of the file: immutable, created by the decoder only, attached to the metadata like the other labels
        var header = typeof(Ktx2Header);
        Assert.Equal("Meziantou.Framework.Imaging.Metadata", header.Namespace);
        Assert.True(header.IsPublic && header.IsSealed && header.IsClass);
        Assert.True(typeof(IEquatable<Ktx2Header>).IsAssignableFrom(header));
        Assert.Empty(header.GetConstructors());
        Assert.All(header.GetProperties(BindingFlags.Instance | BindingFlags.Public), property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        var property = GetDeclaredProperty(typeof(ImageMetadata), nameof(ImageMetadata.Ktx2Header));
        Assert.Equal(header, property.PropertyType);
        Assert.NotNull(property.GetSetMethod());
    }

    [Fact]
    public void PixelFormatAndAllocatorAdditionsHaveTheirPublicShape()
    {
        // New pixel formats are appended: the values of the existing members never change
        Assert.Equal(18, (int)PixelFormat.Cmyk64);
        Assert.Equal([19, 20, 21, 22], new[] { PixelFormat.GrayFloat, PixelFormat.GrayHalf, PixelFormat.LaFloat, PixelFormat.LaHalf }.Select(format => (int)format));
        Assert.Equal(22, Enum.GetValues<PixelFormat>().Max(format => (int)format));
        foreach (var type in new[] { typeof(GrayFloat), typeof(GrayHalf), typeof(LaFloat), typeof(LaHalf) })
        {
            Assert.True(type.IsValueType && type.IsPublic && type.IsLayoutSequential);
        }

        // A custom pixel type: static members only, constrained to unmanaged implementations of the interface
        var customPixel = typeof(ICustomPixel<>);
        Assert.True(customPixel.IsInterface);
        Assert.All(customPixel.GetMethods(), method => Assert.True(method.IsStatic && method.IsAbstract, method.Name));
        Assert.Equal(
            [nameof(ICustomPixel<>.FromStorage), nameof(ICustomPixel<>.ToStorage), "get_" + nameof(ICustomPixel<>.StorageFormat)],
            customPixel.GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.True(typeof(CustomPixelRowAction<>).IsSubclassOf(typeof(Delegate)));

        // The allocator: an abstract class with a protected constructor, set on the immutable configuration
        var allocator = typeof(ImageBufferAllocator);
        Assert.True(allocator.IsPublic && allocator.IsAbstract && !allocator.IsSealed);
        Assert.True(Assert.Single(allocator.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).IsFamily);
        Assert.True(allocator.GetMethod(nameof(ImageBufferAllocator.GetBufferLength))!.IsVirtual);
        var property = GetDeclaredProperty(typeof(ImageConfiguration), nameof(ImageConfiguration.BufferAllocator));
        Assert.Equal(allocator, property.PropertyType);
        Assert.True(property.GetSetMethod()!.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)));
    }

    [Fact]
    public void TextureEncoderAdditionsHaveTheirPublicShape()
    {
        foreach (var type in new[] { typeof(DdsEncoder), typeof(Ktx2Encoder) })
        {
            Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
            Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
            Assert.Equal(typeof(TextureCompressionQuality), GetDeclaredProperty(type, "Quality").PropertyType);
        }

        Assert.Equal(
            [nameof(DdsEncoder.AllowBitDepthReduction), nameof(DdsEncoder.AllowChannelReduction), nameof(DdsEncoder.Format), nameof(DdsEncoder.GenerateMipmaps), nameof(DdsEncoder.HeaderVersion), nameof(DdsEncoder.Quality), nameof(DdsEncoder.TextureFormat)],
            typeof(DdsEncoder).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            [nameof(Ktx2Encoder.AllowBitDepthReduction), nameof(Ktx2Encoder.AllowChannelReduction), nameof(Ktx2Encoder.Format), nameof(Ktx2Encoder.GenerateMipmaps), nameof(Ktx2Encoder.Quality), nameof(Ktx2Encoder.Supercompression), nameof(Ktx2Encoder.TextureFormat)],
            typeof(Ktx2Encoder).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(DdsTextureFormat?), GetDeclaredProperty(typeof(DdsEncoder), nameof(DdsEncoder.TextureFormat)).PropertyType);
        Assert.Equal(typeof(Ktx2TextureFormat?), GetDeclaredProperty(typeof(Ktx2Encoder), nameof(Ktx2Encoder.TextureFormat)).PropertyType);

        // The values of the enumerations are their identity: appended, never renumbered
        Assert.Equal([0, 1], Enum.GetValues<TextureCompressionQuality>().Select(value => (int)value));
        Assert.Equal([0, 1, 2], Enum.GetValues<DdsHeaderVersion>().Select(value => (int)value));
        Assert.Equal([0, 3], Enum.GetValues<Ktx2Supercompression>().Select(value => (int)value));
        Assert.Equal(Enumerable.Range(0, 24), Enum.GetValues<DdsTextureFormat>().Select(value => (int)value));
        Assert.Equal(Enumerable.Range(0, 39), Enum.GetValues<Ktx2TextureFormat>().Select(value => (int)value));
        Assert.Equal(23, (int)DdsTextureFormat.Bc7UnormSrgb);
        Assert.Equal(38, (int)Ktx2TextureFormat.Astc12x12);

        // A collection of texture surfaces can be created and filled
        Assert.NotNull(typeof(ImageCollection).GetMethod(nameof(ImageCollection.Add), [typeof(Image), typeof(TextureSurface)]));
    }

    [Theory]
    [InlineData(typeof(UnknownImageFormatException))]
    [InlineData(typeof(InvalidImageContentException))]
    [InlineData(typeof(UnsupportedImageFeatureException))]
    [InlineData(typeof(ImageResourceLimitException))]
    public void ContentExceptionsDeriveFromImageException(Type type) => Assert.True(type.IsSubclassOf(typeof(ImageException)));

    [Fact]
    public void ExceptionsHaveTheStandardConstructors()
    {
        // Callers and tests can create every exception type of the taxonomy the usual way
        foreach (var type in LibraryAssembly.GetExportedTypes().Where(type => type.IsSubclassOf(typeof(Exception))))
        {
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
            Assert.NotNull(type.GetConstructor([typeof(string)]));
            Assert.NotNull(type.GetConstructor([typeof(string), typeof(Exception)]));
        }
    }

    [Fact]
    public void EnumsDefineTheirDefaultValue()
    {
        // default(TEnum) is always a documented member (the default of the corresponding option). ExifOrientation is the
        // exception: its values are the EXIF tag values 1-8, and ImageMetadata.Orientation rejects 0
        Assert.False(Enum.IsDefined(default(ExifOrientation)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageMetadata().Orientation = default);
        Assert.All(LibraryAssembly.GetExportedTypes().Where(type => type.IsEnum && type != typeof(ExifOrientation)), type => Assert.True(Enum.IsDefined(type, Activator.CreateInstance(type)!), $"{type} has no member with the value 0"));
    }

    [Fact]
    public void AsynchronousMethodsFollowTheTaskConventions()
    {
        // Every awaitable public method is named *Async and takes an optional CancellationToken as its last parameter
        var methods = LibraryAssembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && IsAwaitable(method.ReturnType) && method.Name != nameof(IAsyncDisposable.DisposeAsync))
            .ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.EndsWith("Async", method.Name, StringComparison.Ordinal);
            var last = method.GetParameters()[^1];
            Assert.True(last.ParameterType == typeof(CancellationToken) && last.HasDefaultValue, $"{method.DeclaringType}.{method.Name} must end with an optional CancellationToken");
        });

        // ...and has a synchronous counterpart with the same parameters minus the token
        Assert.All(methods, method =>
        {
            var parameters = method.GetParameters()[..^1].Select(parameter => parameter.ParameterType.IsGenericParameter ? parameter.ParameterType.Name : parameter.ParameterType.ToString()).ToArray();
            var synchronous = method.DeclaringType!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(candidate => candidate.Name == method.Name[..^"Async".Length] && candidate.IsStatic == method.IsStatic)
                .Where(candidate => candidate.GetParameters().Select(parameter => parameter.ParameterType.IsGenericParameter ? parameter.ParameterType.Name : parameter.ParameterType.ToString()).SequenceEqual(parameters, StringComparer.Ordinal));
            Assert.NotEmpty(synchronous); // the asynchronous method has no synchronous counterpart
        });

        static bool IsAwaitable(Type type)
            => type == typeof(Task) || type == typeof(ValueTask) || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));
    }

    [Fact]
    public void ImagesReadersAndWritersAreDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Image)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ImageReader<Rgba32>)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ImageWriter<Rgba32>)));

        // Frames are borrowed views: they must not be disposable
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(ImageFrame)));
    }

    [Fact]
    public void TypedViewsOverrideUntypedMembersWithCovariantReturnTypes()
    {
        Assert.Equal(typeof(ImageFrameCollection<Rgba32>), GetDeclaredProperty(typeof(Image<Rgba32>), nameof(Image.Frames)).PropertyType);
        Assert.Equal(typeof(ImageFrame<Rgba32>), GetDeclaredProperty(typeof(Image<Rgba32>), nameof(Image.PosterFrame)).PropertyType);
        Assert.Equal(typeof(ImageFrame<Rgba32>), GetDeclaredProperty(typeof(ImageFrameCollection<Rgba32>), "Item").PropertyType);
        Assert.Equal(typeof(Image<Rgba32>), typeof(Image<Rgba32>).GetMethod(nameof(Image.Clone), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(Image<Rgba32>), typeof(Image<Rgba32>).GetMethod(nameof(Image.CloneFrame), [typeof(int)])!.ReturnType);
    }

    private static PropertyInfo GetDeclaredProperty(Type type, string name)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Single(property => property.Name == name);

    [Fact]
    public void LibraryOnlyReferencesFrameworkAssemblies()
    {
        // The package is fully managed: no native or third-party runtime dependency. Its dependencies are four packages of
        // this repository: Unicode, for the text algorithms of text layout, Unicode.Dictionaries, for the words of the
        // scripts written without spaces, Fonts, the font reader and the shaper, and Hashing, for the CRC-32 of PNG and
        // the Adler-32 of zlib.
        // 'netstandard' is a framework facade that code-coverage instrumentation (CI) may add to the instrumented assembly.
        Assert.All(LibraryAssembly.GetReferencedAssemblies(), name => Assert.True(
            name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.Name is "netstandard" or "Meziantou.Framework.Unicode" or "Meziantou.Framework.Unicode.Dictionaries" or "Meziantou.Framework.Fonts" or "Meziantou.Framework.Hashing",
            $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void LibraryTargetsNet10OrNet11()
    {
        var framework = LibraryAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Contains(framework, new[] { ".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v11.0" });
    }
}
