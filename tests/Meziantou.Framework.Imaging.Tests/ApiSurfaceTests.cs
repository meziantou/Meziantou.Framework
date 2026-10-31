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
        // No codec plug-in, custom pixel or custom frame contract in v1: base types cannot be derived outside the library
        var accessibleConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly);
        Assert.Empty(accessibleConstructors);
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
        string[] avifOnly = [nameof(AvifEncoder.AutoTiling), nameof(AvifEncoder.CleanAperture), nameof(AvifEncoder.PremultiplyAlpha)];
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
            [nameof(JpegXlEncoder.Compression), nameof(JpegXlEncoder.DurationRounding), nameof(JpegXlEncoder.Effort), nameof(JpegXlEncoder.Format), nameof(JpegXlEncoder.Progressive), nameof(JpegXlEncoder.Quality)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
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
            [nameof(Jpeg2000Encoder.Compression), nameof(Jpeg2000Encoder.Container), nameof(Jpeg2000Encoder.DecompositionLevels), nameof(Jpeg2000Encoder.Format), nameof(Jpeg2000Encoder.ProgressionOrder), nameof(Jpeg2000Encoder.Quality), nameof(Jpeg2000Encoder.QualityLayers), nameof(Jpeg2000Encoder.TileHeight), nameof(Jpeg2000Encoder.TileWidth)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(Jpeg2000Compression), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Compression)).PropertyType);
        Assert.Equal(typeof(Jpeg2000Container), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Container)).PropertyType);
        Assert.Equal(typeof(int), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.Quality)).PropertyType);
        Assert.Equal(typeof(IReadOnlyList<Jpeg2000QualityLayer>), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.QualityLayers)).PropertyType);
        Assert.Equal(typeof(Jpeg2000ProgressionOrder), GetDeclaredProperty(type, nameof(Jpeg2000Encoder.ProgressionOrder)).PropertyType);

        // The defaults are the zero values
        Assert.Equal([nameof(Jpeg2000Compression.Lossless), nameof(Jpeg2000Compression.Lossy)], Enum.GetNames<Jpeg2000Compression>());
        Assert.Equal([nameof(Jpeg2000Container.Jp2), nameof(Jpeg2000Container.Codestream)], Enum.GetNames<Jpeg2000Container>());
        Assert.Equal(0, (int)Jpeg2000Compression.Lossless);
        Assert.Equal(0, (int)Jpeg2000Container.Jp2);

        // The progression orders have the values of the COD marker segment (T.800, Table A.16)
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<Jpeg2000ProgressionOrder>().Select(order => (int)order));
        Assert.Equal([nameof(Jpeg2000ProgressionOrder.Lrcp), nameof(Jpeg2000ProgressionOrder.Rlcp), nameof(Jpeg2000ProgressionOrder.Rpcl), nameof(Jpeg2000ProgressionOrder.Pcrl), nameof(Jpeg2000ProgressionOrder.Cprl)], Enum.GetNames<Jpeg2000ProgressionOrder>());
    }

    [Fact]
    public void OpenExrAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(17, (int)ImageFormat.OpenExr);
        Assert.Equal(17, Enum.GetValues<ImageFormat>().Max(format => (int)format));

        var type = typeof(OpenExrEncoder);
        Assert.Equal("Meziantou.Framework.Imaging.Formats", type.Namespace);
        Assert.True(type.IsPublic && type.IsSealed && type.IsSubclassOf(typeof(ImageEncoder)));
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            [nameof(OpenExrEncoder.AllowBitDepthReduction), nameof(OpenExrEncoder.Compression), nameof(OpenExrEncoder.Format), nameof(OpenExrEncoder.PixelType)],
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(OpenExrCompression), GetDeclaredProperty(type, nameof(OpenExrEncoder.Compression)).PropertyType);
        Assert.Equal(typeof(OpenExrPixelType?), GetDeclaredProperty(type, nameof(OpenExrEncoder.PixelType)).PropertyType);

        // The compressions have the values of the format; the pixel types have no default value but null
        Assert.Equal([nameof(OpenExrCompression.None), nameof(OpenExrCompression.Rle), nameof(OpenExrCompression.Zips), nameof(OpenExrCompression.Zip), nameof(OpenExrCompression.Piz)], Enum.GetNames<OpenExrCompression>());
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<OpenExrCompression>().Select(value => (int)value));
        Assert.Equal([nameof(OpenExrPixelType.Half), nameof(OpenExrPixelType.Float)], Enum.GetNames<OpenExrPixelType>());
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
        // The package is fully managed: no native or third-party runtime dependency. Its dependencies are three packages of
        // this repository: Unicode, for the text algorithms of text layout, Fonts, the font reader and the shaper, and
        // Hashing, for the CRC-32 of PNG and the Adler-32 of zlib.
        // 'netstandard' is a framework facade that code-coverage instrumentation (CI) may add to the instrumented assembly.
        Assert.All(LibraryAssembly.GetReferencedAssemblies(), name => Assert.True(
            name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.Name is "netstandard" or "Meziantou.Framework.Unicode" or "Meziantou.Framework.Fonts" or "Meziantou.Framework.Hashing",
            $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void LibraryTargetsNet10OrNet11()
    {
        var framework = LibraryAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Contains(framework, new[] { ".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v11.0" });
    }
}
