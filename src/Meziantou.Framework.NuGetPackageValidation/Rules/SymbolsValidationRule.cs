using System.Data;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal sealed partial class SymbolsValidationRule : NuGetPackageValidationRule
{
    private const ushort PortableCodeViewVersionMagic = 0x504d;

    private static readonly Guid SourceLinkId = new(0xCC110556, 0xA091, 0x4D38, 0x9F, 0xEC, 0x25, 0xAB, 0x9A, 0x35, 0x1A, 0x6A);
    private static readonly Guid EmbeddedSourceId = new(0x0E8A571B, 0x6926, 0x466E, 0xB4, 0xAD, 0x8A, 0xB0, 0x46, 0x11, 0xF5, 0xFE);
    private static readonly Guid CompilerFlagsId = new(0xB5FEEC05, 0x8CD0, 0x4A83, 0x96, 0xDA, 0x46, 0x62, 0x84, 0xBB, 0x4B, 0xD8);
    // https://github.com/dotnet/runtime/blob/18d0ead1f33808def27f8b57ccd907ec9efb14ac/docs/design/specs/PortablePdb-Metadata.md#document-table-0x30
    private static readonly Guid HashAlgorithmSha1 = new(0xFF1816EC, 0xAA5E, 0x4D10, 0x87, 0xF7, 0x6F, 0x49, 0x63, 0x83, 0x34, 0x60);
    private static readonly Guid HashAlgorithmSha256 = new(0x8829D00F, 0x11B8, 0x4213, 0x87, 0x8B, 0x77, 0x0E, 0x85, 0x97, 0xAC, 0x16);

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA1 is not use for crypto")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "False-positive")]
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
            if (IsSatelliteAssembly(item))
                continue;

            var itemExtension = Path.GetExtension(item);
            if (string.Equals(itemExtension, ".dll", StringComparison.OrdinalIgnoreCase) || string.Equals(itemExtension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Check if this is a .NET assembly
                if (!await IsDotNetAssembly(context, item).ConfigureAwait(false))
                    continue;

                // Symbols can be embedded, in a pdb file next to the dll, or in a snupkg file, or from a symbol server
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
                    var pdbChecksums = entries.Where(e => e.Type == DebugDirectoryEntryType.PdbChecksum).Select(peReader.ReadPdbChecksumDebugDirectoryData).ToArray();

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
                    if (metadataReaderProvider is null && codeViewEntry.DataSize != 0)
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
                        if (metadataReaderProvider is null && context.SymbolPackage is not null)
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

                        // load pdb from the symbol server
                        if (metadataReaderProvider is null)
                        {
                            // Portable PDBs, see: https://github.com/dotnet/symstore/blob/83032682c049a2b879790c615c27fbc785b254eb/src/Microsoft.SymbolStore/KeyGenerators/PortablePDBFileKeyGenerator.cs#L84
                            // Windows PDBs, see: https://github.com/dotnet/symstore/blob/83032682c049a2b879790c615c27fbc785b254eb/src/Microsoft.SymbolStore/KeyGenerators/PDBFileKeyGenerator.cs#L52
                            var data = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
                            var isPortable = codeViewEntry.MinorVersion == PortableCodeViewVersionMagic;
                            var signature = data.Guid;
                            var age = data.Age;

                            var symbolId = isPortable
                                ? signature.ToString("N", CultureInfo.InvariantCulture) + "FFFFFFFF"
                                : string.Format(CultureInfo.InvariantCulture, "{0}{1:x}", signature.ToString("N", CultureInfo.InvariantCulture), age);

                            foreach (var symbolServer in context.SymbolServers)
                            {
                                foreach (var checksum in pdbChecksums)
                                {
                                    var url = symbolServer + Path.GetFileName(pdbPath) + "/" + symbolId + "/" + Path.GetFileName(pdbPath);
                                    var checksumHeader = checksum.AlgorithmName + ":" + Convert.ToHexString(checksum.Checksum.AsSpan());
                                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                                    request.Headers.Add("SymbolChecksum", checksumHeader);

                                    using var response = await context.SendHttpRequestAsync(request, context.CancellationToken).ConfigureAwait(false);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        var pdbData = await response.Content.ReadAsStreamAsync(context.CancellationToken).ConfigureAwait(false);
                                        await using (pdbData.ConfigureAwait(false))
                                        {
                                            if (pdbData.CanSeek)
                                            {
                                                metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbData, MetadataStreamOptions.PrefetchMetadata);
                                            }
                                            else
                                            {
                                                using var ms = new MemoryStream();
                                                await pdbData.CopyToAsync(ms, context.CancellationToken).ConfigureAwait(false);
                                                ms.Seek(0, SeekOrigin.Begin);
                                                metadataReaderProvider = MetadataReaderProvider.FromPortablePdbStream(ms, MetadataStreamOptions.PrefetchMetadata);
                                            }

                                            break;
                                        }
                                    }
                                }

                                if (metadataReaderProvider is not null)
                                    break;
                            }
                        }

                        // Ensure the pdb is valid for the dll
                        if (metadataReaderProvider is not null)
                        {
                            var pdbHeader = metadataReaderProvider.GetMetadataReader().DebugMetadataHeader;
                            if (pdbHeader is null)
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

                    if (metadataReaderProvider is null)
                    {
                        if (PackageFileExists(context.Package, pdbPath) || (context.SymbolPackage is not null && PackageFileExists(context.SymbolPackage, pdbPath)))
                        {
                            context.ReportError(ErrorCodes.FullPdb, "Symbol file is not a portable PDB", fileName: item,
                                helpText: "Update the csproj file with '<DebugType>Portable</DebugType>' or '<DebugType>embedded</DebugType>' (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation?WT.mc_id=DT-MVP-5003978#debugtype)");
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
                        context.ReportError(ErrorCodes.CompilerFlagsNotPresent, "Compiler flags not present", fileName: item, helpText: "Use at least .NET SDK 5.0.300 to use a compiler that adds compiler flags to the PDB files.");
                    }
                    else if (!int.TryParse(compilerInfo.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var compilerVersion))
                    {
                        context.ReportError(ErrorCodes.InvalidCompilerVersion, $"Compiler version '{compilerInfo.Version}' is invalid", fileName: item);
                    }
                    else if (compilerVersion < 2)
                    {
                        context.ReportError(ErrorCodes.CompilerDoesNotSupportReproducibleBuilds, "Compiler is too old and does not support reproducible builds", fileName: item, helpText: "Use at least .NET SDK 2.1.300");
                    }

                    foreach (var documentHandle in reader.Documents)
                    {
                        var document = reader.GetDocument(documentHandle);
                        if (document.Name.IsNil || document.Language.IsNil || document.Hash.IsNil || document.HashAlgorithm.IsNil)
                            continue;

                        var name = reader.GetString(document.Name);
                        if (!DeterministicFileNameRegex().IsMatch(name))
                        {
                            context.ReportError(ErrorCodes.NonDeterministic, $"Symbol file contains non-deterministic file path '{name}'", fileName: item);
                        }

                        var isEmbeddedFile = IsEmbeddedDocument(reader, documentHandle);
                        var url = sourceLink?.GetUrl(name);
                        if (!isEmbeddedFile && url is null)
                        {
                            context.ReportError(ErrorCodes.SourceFileNotAccessible, $"Source file '{name}' is not accessible from the symbols", fileName: item);
                        }
                        else if (!isEmbeddedFile && url is not null)
                        {
                            if (!context.IsRuleExcluded(ErrorCodes.UrlIsNotAccessible) && !context.IsRuleExcluded(ErrorCodes.FileHashIsNotValid))
                            {
                                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                                {
                                    context.ReportError(ErrorCodes.UrlIsNotAccessible, $"Source file '{url}' is not a valid uri", fileName: item);
                                    continue;
                                }

                                try
                                {
                                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                                    using var response = await context.SendHttpRequestAsync(request, context.CancellationToken).ConfigureAwait(false);
                                    if (!response.IsSuccessStatusCode)
                                    {
                                        var message = string.Create(CultureInfo.InvariantCulture, $"Source file '{url}' is not accessible (HTTP status code = {(int)response.StatusCode})");
                                        context.ReportError(ErrorCodes.UrlIsNotAccessible, message, fileName: item);
                                    }
                                    else
                                    {
                                        if (document.Hash.IsNil)
                                        {
                                            context.ReportError(ErrorCodes.FileHashIsNotProvided, $"Source file '{url}' has no hash", fileName: item);
                                        }
                                        else
                                        {
                                            var data = await response.Content.ReadAsByteArrayAsync(context.CancellationToken).ConfigureAwait(false);
                                            var hashAlgorithm = reader.GetGuid(document.HashAlgorithm);
                                            if (hashAlgorithm == HashAlgorithmSha1)
                                            {
                                                var expectedHash = reader.GetBlobBytes(document.Hash);
                                                if (!MatchHash(data, expectedHash, HashAlgorithmName.SHA1))
                                                {
                                                    context.ReportError(ErrorCodes.FileHashIsNotValid, $"Source file '{url}' hash differ from the expected hash", fileName: item);
                                                }
                                            }
                                            else if (hashAlgorithm == HashAlgorithmSha256)
                                            {
                                                var expectedHash = reader.GetBlobBytes(document.Hash);
                                                if (!MatchHash(data, expectedHash, HashAlgorithmName.SHA256))
                                                {
                                                    context.ReportError(ErrorCodes.FileHashIsNotValid, $"Source file '{url}' hash differ from the expected hash", fileName: item);
                                                }
                                            }
                                            else
                                            {
                                                context.ReportError(ErrorCodes.NotSupportedHashAlgorithm, $"Source file '{url}' hash algorithm '{hashAlgorithm}' is not supported", fileName: item);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    context.ReportError(ErrorCodes.UrlIsNotAccessible, $"Source file '{url}' is not accessible: {ex}", fileName: item);
                                }
                            }
                        }
                    }
                }
                finally
                {
#pragma warning disable CA1508 // Avoid dead conditional code
                    peReader?.Dispose();
#pragma warning restore CA1508

                    metadataReaderProvider?.Dispose();
                    if (pdbStreamSeekable is not null)
                    {
                        await pdbStreamSeekable.DisposeAsync().ConfigureAwait(false);
                    }

                    if (pdbStream is not null)
                    {
                        await pdbStream.DisposeAsync().ConfigureAwait(false);
                    }

                    if (dllStream is not null)
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

    private static bool MatchHash(ReadOnlySpan<byte> content, ReadOnlySpan<byte> expectedHash, HashAlgorithmName hashAlgorithmName)
    {
        using var hash = IncrementalHash.CreateHash(hashAlgorithmName);

        hash.AppendData(content);
        var actualHash = hash.GetHashAndReset();
        if (expectedHash.SequenceEqual(actualHash))
            return true;

        actualHash = CalculateHashWithLineBreakSubstituted(hash, content, "\r\n"u8);
        if (expectedHash.SequenceEqual(actualHash))
            return true;

        actualHash = CalculateHashWithLineBreakSubstituted(hash, content, "\n"u8);
        if (expectedHash.SequenceEqual(actualHash))
            return true;

        return false;

        static byte[] CalculateHashWithLineBreakSubstituted(IncrementalHash incrementalHash, ReadOnlySpan<byte> content, ReadOnlySpan<byte> newLine)
        {
            while (!content.IsEmpty)
            {
                var index = content.IndexOf((byte)'\n');
                if (index < 0)
                {
                    incrementalHash.AppendData(content);
                    return incrementalHash.GetHashAndReset();
                }

                if (index > 0 && content[index - 1] == (byte)'\r')
                {
                    incrementalHash.AppendData(content[..(index - 1)]);
                }
                else
                {
                    incrementalHash.AppendData(content[..index]);
                }

                incrementalHash.AppendData(newLine);
                content = content[(index + 1)..];
            }

            return incrementalHash.GetHashAndReset();
        }
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
            if (Documents is null)
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
                    var path = m.Groups[1].Value.Replace('\\', '/');
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

    [GeneratedRegex("^/_[0-9]*/", RegexOptions.None, matchTimeoutMilliseconds: -1)]
    private static partial Regex DeterministicFileNameRegex();
}
