using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed class XmlDocumentationMustBePresentValidationRule : NuGetPackageValidationRule
{
    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var groups = await context.Package.GetLibItemsAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                if (IsSatelliteAssembly(item))
                    continue;

                if (!PackageFileExists(context.Package, Path.ChangeExtension(item, ".xml")))
                {
                    if (await IsDotNetLibrary(context, item).ConfigureAwait(false))
                    {
                        context.ReportError(ErrorCodes.XmlDocumentationNotFound, "XML documentation not found", item);
                    }
                }
            }
        }
    }

    private static async Task<bool> IsDotNetLibrary(NuGetPackageValidationContext context, string fileName)
    {
        var stream = await context.Package.GetStreamAsync(fileName, context.CancellationToken).ConfigureAwait(false);
        try
        {
            var seekableStream = await CreateSeekableStream(stream, context.CancellationToken).ConfigureAwait(false);
            try
            {
                using var peReader = new PEReader(seekableStream);
                if (!peReader.HasMetadata)
                    return false; // File does not have CLI metadata.

                var reader = peReader.GetMetadataReader();
                if (!reader.IsAssembly)
                    return false;

                return peReader.PEHeaders.IsDll;
            }
            catch
            {
                // The assembly is not valid
                return false;
            }
            finally
            {
                await seekableStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
