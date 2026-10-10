using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

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
        typeof(PngEncoder),
        typeof(GifEncoder),
        typeof(JpegEncoder),
    ];

    [Fact]
    public void PublicTypesAreInDocumentedNamespaces()
    {
        string[] allowed = ["Meziantou.Framework.Imaging", "Meziantou.Framework.Imaging.Metadata", "Meziantou.Framework.Imaging.Formats"];
        Assert.All(LibraryAssembly.GetExportedTypes(), type => Assert.Contains(type.Namespace, allowed));
    }

    [Theory]
    [InlineData(typeof(Image))]
    [InlineData(typeof(ImageFrame))]
    [InlineData(typeof(ImageFrameCollection))]
    [InlineData(typeof(ImageEncoder))]
    [InlineData(typeof(AutoCropAnalysis))]
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
        // The package is fully managed: no native or third-party runtime dependency.
        // 'netstandard' is a framework facade that code-coverage instrumentation (CI) may add to the instrumented assembly.
        Assert.All(LibraryAssembly.GetReferencedAssemblies(), name => Assert.True(
            name.Name!.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.Name == "netstandard",
            $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void LibraryTargetsNet10OrNet11()
    {
        var framework = LibraryAssembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Contains(framework, new[] { ".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v11.0" });
    }
}
