using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class AssembliesMustBeOptimizedMustBeSetValidationRule : NuGetPackageValidationRule
{
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
                    try
                    {
                        using var peReader = new PEReader(ms);
                        var metadata = peReader.GetMetadataReader();
                        foreach (var attributeHandle in metadata.CustomAttributes)
                        {
                            var attribute = metadata.GetCustomAttribute(attributeHandle);
                            var ctor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                            var attrType = GetFullName(metadata, (TypeReferenceHandle)ctor.Parent);
                            if (attrType == "System.Diagnostics.DebuggableAttribute")
                            {
                                var value = metadata.GetBlobBytes(attribute.Value);

                                // https://stackoverflow.com/a/3533876
                                var isAssemblyOptimized = value.AsSpan().SequenceEqual(new byte[] { 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 });
                                if (!isAssemblyOptimized)
                                {
                                    context.ReportError(ErrorCodes.AssemblyIsNotOptimized, "Assembly is not optimized", fileName: file,
                                        helpText: "Build the package using the Release configuration: 'dotnet pack --configuration Release'. Alternatively you can add '<Optimize>true</Optimize>' in the csproj (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation?WT.mc_id=DT-MVP-5003978#optimize)");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Maybe not a .NET assembly
                    }
                }
                finally
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static string GetFullName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            var name = reader.GetString(type.Name);
            var ns = type.Namespace.IsNil ? null : reader.GetString(type.Namespace);
            if (ns is null)
                return name;

            return ns + "." + name;
        }

        throw new NotSupportedException();
    }
}
