using System.Data;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed partial class SymbolsValidationRule : NuGetPackageValidationRule
{
    private static readonly Guid SourceLinkId = new(0xCC110556, 0xA091, 0x4D38, 0x9F, 0xEC, 0x25, 0xAB, 0x9A, 0x35, 0x1A, 0x6A);
    private static readonly Guid EmbeddedSourceId = new(0x0E8A571B, 0x6926, 0x466E, 0xB4, 0xAD, 0x8A, 0xB0, 0x46, 0x11, 0xF5, 0xFE);
    private static readonly Guid CompilerFlagsId = new(0xB5FEEC05, 0x8CD0, 0x4A83, 0x96, 0xDA, 0x46, 0x62, 0x84, 0xBB, 0x4B, 0xD8);

    public override async Task ExecuteAsync(NuGetPackageValidationContext context)
    {
        var allItems = new List<string>();
        var groups = await context.Package.GetLibItemsAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var group in groups)
        {
            allItems.AddRange(group.Items);
        }

        var analyzers = await context.Package.GetFilesAsync("analyzers", context.CancellationToken).ConfigureAwait(false);
        allItems.AddRange(analyzers);

        foreach (var item in allItems)
        {
            var itemExtension = Path.GetExtension(item);
            if (string.Equals(itemExtension, ".dll", StringComparison.OrdinalIgnoreCase) || string.Equals(itemExtension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Check if this is a .NET assembly
                if (!await IsDotNetAssembly(context, item).ConfigureAwait(false))
                    continue;


                // Symbols can be embeded, in a pdb file next to the dll, or in a snupkg file
                var pdbPath = Path.ChangeExtension(item, ".pdb");
                Stream? dllStream = null;
                Stream? pdbStream = null;
                Stream? dllStreamSeekable = null;
                Stream? pdbStreamSeekable = null;
                PEReader? peReader = null;
                MetadataReaderProvider? metadataReaderProvider = null;
                try
                {
                    dllStream = await context.Package.GetStreamAsync(item, context.CancellationToken).ConfigureAwait(false);
                    dllStreamSeekable = await CreateSeekableStream(dllStream, context.CancellationToken).ConfigureAwait(false);
                    peReader = new PEReader(dllStreamSeekable);
                    var entries = peReader.ReadDebugDirectory();
                    var codeViewEntry = entries.FirstOrDefault(e => e.IsPortableCodeView);

                    // Try to load embedded pdb
                    try
                    {
                        var metadataReader = peReader.GetMetadataReader();
                        var entry = peReader.ReadDebugDirectory().Where(de => de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).ToArray();
                        if (entry.Length > 0)
                        {
                            metadataReaderProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry[0]);
                        }
                    }
                    catch
                    {
                    }

                    // load pdb next to the file if a portable pdb is expected
                    if (metadataReaderProvider == null && codeViewEntry.DataSize != 0)
                    {
                        try
                        {
                            pdbStream = await context.Package.GetStreamAsync(pdbPath, context.CancellationToken).ConfigureAwait(false);
                            pdbStreamSeekable = await CreateSeekableStream(pdbStream, context.CancellationToken).ConfigureAwait(false);
                            metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStreamSeekable);
                        }
                        catch (FileNotFoundException)
                        {
                        }

                        // load pdb from the symbol package
                        if (metadataReaderProvider == null && context.SymbolPackage != null)
                        {
                            try
                            {
                                pdbStream = await context.SymbolPackage.GetStreamAsync(pdbPath, context.CancellationToken).ConfigureAwait(false);
                                pdbStreamSeekable = await CreateSeekableStream(pdbStream, context.CancellationToken).ConfigureAwait(false);
                                metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStreamSeekable);
                            }
                            catch (FileNotFoundException)
                            {
                            }
                        }

                        // Ensure the pdb is valid for the dll
                        if (metadataReaderProvider != null)
                        {
                            var pdbHeader = metadataReaderProvider.GetMetadataReader().DebugMetadataHeader;
                            if (pdbHeader == null)
                            {
                                metadataReaderProvider = null;
                            }
                            else
                            {
                                // Compute assembly content id
                                var data = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
                                var id = new BlobContentId(data.Guid, codeViewEntry.Stamp);

                                if (new BlobContentId(pdbHeader.Id) != id)
                                {
                                    context.ReportError(ErrorCodes.PdbDoesNotMatchAssembly, $"Symbol file does not match the assembly", fileName: item);
                                    continue;
                                }
                            }
                        }
                    }

                    if (metadataReaderProvider == null)
                    {
                        if (PackageFileExists(context.Package, pdbPath) || (context.SymbolPackage != null && PackageFileExists(context.SymbolPackage, pdbPath)))
                        {
                            context.ReportError(ErrorCodes.FullPdb, "Symbol file is not a portable PDB", fileName: item);
                            continue;
                        }

                        context.ReportError(ErrorCodes.SymbolsNotFound, "Symbol file not found", fileName: item);
                        continue;
                    }

                    MetadataReader reader;
                    try
                    {
                        reader = metadataReaderProvider.GetMetadataReader();
                    }
                    catch (BadImageFormatException)
                    {
                        context.ReportError(ErrorCodes.FullPdb, "Symbol file is not a portable PDB", fileName: item);
                        continue;
                    }

                    var sourceLink = GetSourceLink(reader);
                    var compilerInfo = GetCompilerFlags(reader);
                    if (string.IsNullOrEmpty(compilerInfo.Version))
                    {
                        context.ReportError(ErrorCodes.CompilerFlagsNotPresent, $"Compiler flags not present", fileName: item);
                    }
                    else if (!int.TryParse(compilerInfo.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var compilerVersion))
                    {
                        context.ReportError(ErrorCodes.InvalidCompilerVersion, $"Compiler version '{compilerInfo.Version}' is invalid", fileName: item);
                    }
                    else if (compilerVersion < 2)
                    {
                        context.ReportError(ErrorCodes.CompilerDoesNotSupportReproducibleBuilds, $"Compiler is too old and does not support reproducible builds", fileName: item);
                    }

                    foreach (var documentHandle in reader.Documents)
                    {
                        var document = reader.GetDocument(documentHandle);
                        if (document.Name.IsNil || document.Language.IsNil || document.Hash.IsNil || document.HashAlgorithm.IsNil)
                            continue;

                        var name = reader.GetString(document.Name);
                        if (!Regex.IsMatch(name, "^/_[0-9]*/", RegexOptions.None, Timeout.InfiniteTimeSpan))
                        {
                            context.ReportError(ErrorCodes.NonDeterministic, $"Symbol file contains non-deterministic file path '{name}'", fileName: item);
                        }

                        var isEmbeddedFile = IsEmbeddedDocument(reader, documentHandle);
                        var url = sourceLink?.GetUrl(name);
                        if (!isEmbeddedFile && url == null)
                        {
                            context.ReportError(ErrorCodes.SourceFileNotAccessible, $"Source file '{name}' is not accessible from the symbols", fileName: item);
                        }
                    }
                }
                finally
                {
                    peReader?.Dispose();
                    metadataReaderProvider?.Dispose();
                    if (pdbStreamSeekable != null)
                    {
                        await pdbStreamSeekable.DisposeAsync().ConfigureAwait(false);
                    }
                    if (pdbStream != null)
                    {
                        await pdbStream.DisposeAsync().ConfigureAwait(false);
                    }
                    if (dllStream != null)
                    {
                        await dllStream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static async Task<bool> IsDotNetAssembly(NuGetPackageValidationContext context, string fileName)
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
                return reader.IsAssembly;
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

    private static SourceLinkJson? GetSourceLink(MetadataReader reader)
    {
        var blobHandle = default(BlobHandle);
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var cdi = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(cdi.Kind) == SourceLinkId)
            {
                blobHandle = cdi.Value;
            }
        }

        if (blobHandle.IsNil)
            return null;

        return JsonSerializer.Deserialize(reader.GetBlobBytes(blobHandle), SourceLinkContext.Default.SourceLinkJson);
    }

    private static bool IsEmbeddedDocument(MetadataReader reader, DocumentHandle documentHandle)
    {
        foreach (var cdih in reader.GetCustomDebugInformation(documentHandle))
        {
            var cdi = reader.GetCustomDebugInformation(cdih);
            if (reader.GetGuid(cdi.Kind) == EmbeddedSourceId)
                return true;
        }

        return false;
    }

    private static CompilerData GetCompilerFlags(MetadataReader reader)
    {
        string? version = null;
        foreach (var cdih in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var customDebugInformation = reader.GetCustomDebugInformation(cdih);
            if (reader.GetGuid(customDebugInformation.Kind) == CompilerFlagsId)
            {
                var blobReader = reader.GetBlobReader(customDebugInformation.Value);

                // Compiler flag bytes are UTF-8 null-terminated key-value pairs
                var nullIndex = blobReader.IndexOf(0);
                while (nullIndex >= 0)
                {
                    var key = blobReader.ReadUTF8(nullIndex);

                    // Skip the null terminator
                    blobReader.ReadByte();

                    nullIndex = blobReader.IndexOf(0);
                    var value = blobReader.ReadUTF8(nullIndex);

                    // Skip the null terminator
                    blobReader.ReadByte();

                    nullIndex = blobReader.IndexOf(0);

                    // key and value now have strings containing serialized compiler flag information
                    if (key == "version")
                    {
                        version = value;
                    }
                }
            }
        }

        return new CompilerData(version);
    }

    private sealed record CompilerData(string? Version);

    [JsonSerializable(typeof(SourceLinkJson))]
    private sealed partial class SourceLinkContext : JsonSerializerContext
    {
    }

    private sealed class SourceLinkJson
    {
        [JsonPropertyName("documents")]
        public Dictionary<string, string>? Documents { get; set; }

        public string? GetUrl(string file)
        {
            if (Documents == null)
                return null;

            foreach (var key in Documents.Keys)
            {
                if (key.Contains('*', StringComparison.Ordinal))
                {
                    var pattern = Regex.Escape(key).Replace(@"\*", "(.+)", StringComparison.Ordinal);
                    var m = Regex.Match(file, pattern, RegexOptions.None, Timeout.InfiniteTimeSpan);
                    if (!m.Success)
                        continue;

                    var url = Documents[key];
                    var path = m.Groups[1].Value.Replace(@"\", "/", StringComparison.Ordinal);
                    return url.Replace("*", path, StringComparison.Ordinal);
                }
                else
                {
                    if (!key.Equals(file, StringComparison.Ordinal))
                        continue;

                    return Documents[key];
                }
            }

            return null;
        }
    }
}
