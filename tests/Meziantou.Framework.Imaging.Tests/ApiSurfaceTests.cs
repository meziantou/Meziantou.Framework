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
        typeof(ConvolutionOptions),
        typeof(ConvolutionKernel),
        typeof(AutoCropOptions),
        typeof(IccColorTransformOptions),
        typeof(CmykConversionOptions),
        typeof(PngEncoder),
        typeof(GifEncoder),
        typeof(JpegEncoder),
        typeof(AvifEncoder),
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
    public void JpegXlAdditionsHaveTheirPublicShape()
    {
        // A new format is appended: the values of the existing members never change
        Assert.Equal(14, (int)ImageFormat.JpegXl);
        Assert.Equal(nameof(ImageFormat.JpegXl), Enum.GetName((ImageFormat)14));
        Assert.Equal(14, Enum.GetValues<ImageFormat>().Max(format => (int)format));

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
        // The package is fully managed: no native or third-party runtime dependency. Its one dependency is the Unicode
        // package of this repository, for the text algorithms of text layout.
        // 'netstandard' is a framework facade that code-coverage instrumentation (CI) may add to the instrumented assembly.
        Assert.All(LibraryAssembly.GetReferencedAssemblies(), name => Assert.True(
            name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.Name is "netstandard" or "Meziantou.Framework.Unicode",
            $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void LibraryTargetsNet10OrNet11()
    {
        var framework = LibraryAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Contains(framework, new[] { ".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v11.0" });
    }
}
