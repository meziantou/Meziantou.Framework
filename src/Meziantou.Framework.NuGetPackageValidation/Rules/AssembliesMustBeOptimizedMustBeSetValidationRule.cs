using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class AssembliesMustBeOptimizedMustBeSetValidationRule : NuGetPackageValidationRule
{
    private const string DebuggableAttributeFullName = "System.Diagnostics.DebuggableAttribute";

    // https://stackoverflow.com/a/3533876
    private static ReadOnlySpan<byte> OptimizedDebuggableAttributeValue => [0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00];

    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        foreach (var file in await context.Package.GetFilesAsync(context.CancellationToken).ConfigureAwait(false))
        {
            var extension = Path.GetExtension(file);
            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                var stream = await context.Package.GetStreamAsync(file, context.CancellationToken).ConfigureAwait(false);
                try
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, context.CancellationToken).ConfigureAwait(false);
                    ms.Seek(0, SeekOrigin.Begin);
                    ValidateAssembly(context, ms, file);
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static void ValidateAssembly(NuGetPackageValidationContext context, Stream stream, string file)
    {
        try
        {
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return; // Maybe not a .NET assembly

            var metadata = peReader.GetMetadataReader();
            foreach (var attributeHandle in metadata.CustomAttributes)
            {
                if (!TryGetDebuggableAttributeValue(metadata, attributeHandle, out var value))
                    continue;

                if (!value.AsSpan().SequenceEqual(OptimizedDebuggableAttributeValue))
                {
                    context.ReportError(ErrorCodes.AssemblyIsNotOptimized, "Assembly is not optimized", fileName: file,
                        helpText: "Build the package using the Release configuration: 'dotnet pack --configuration Release'. Alternatively you can add '<Optimize>true</Optimize>' in the csproj (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation?WT.mc_id=DT-MVP-5003978#optimize)");
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Maybe not a .NET assembly
        }
    }

    private static bool TryGetDebuggableAttributeValue(MetadataReader reader, CustomAttributeHandle handle, [NotNullWhen(true)] out byte[]? value)
    {
        try
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (TryGetAttributeTypeFullName(reader, attribute.Constructor, out var fullName) && fullName == DebuggableAttributeFullName)
            {
                value = reader.GetBlobBytes(attribute.Value);
                return true;
            }
        }
        catch (BadImageFormatException)
        {
            // Skip this attribute instead of abandoning the remaining ones
        }

        value = null;
        return false;
    }

    /// <summary>Resolves the full name of the type an attribute instantiates. The constructor is a <see cref="MemberReferenceHandle"/>
    /// when the attribute type comes from another assembly, and a <see cref="MethodDefinitionHandle"/> when it is declared in the
    /// assembly being inspected.</summary>
    private static bool TryGetAttributeTypeFullName(MetadataReader reader, EntityHandle constructor, [NotNullWhen(true)] out string? fullName)
    {
        var declaringType = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => (EntityHandle)reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };

        switch (declaringType.Kind)
        {
            case HandleKind.TypeReference:
                var typeReference = reader.GetTypeReference((TypeReferenceHandle)declaringType);
                fullName = GetFullName(reader, typeReference.Namespace, typeReference.Name);
                return true;

            case HandleKind.TypeDefinition:
                var typeDefinition = reader.GetTypeDefinition((TypeDefinitionHandle)declaringType);
                fullName = GetFullName(reader, typeDefinition.Namespace, typeDefinition.Name);
                return true;

            default:
                // Generic attributes (TypeSpecification) and any handle kind added later
                fullName = null;
                return false;
        }

        static string GetFullName(MetadataReader reader, StringHandle namespaceHandle, StringHandle nameHandle)
        {
            var name = reader.GetString(nameHandle);
            if (namespaceHandle.IsNil)
                return name;

            return reader.GetString(namespaceHandle) + "." + name;
        }
    }
}
