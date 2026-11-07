using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.PublicApiGenerator;

// The members of an enum, used to name the enum values passed to attributes and the enum constants
internal sealed class EnumMetadata
{
    private static readonly Lock LoadedAssembliesCacheLock = new();
    private static readonly Dictionary<string, EnumMetadata?> LoadedAssembliesCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string AssemblyPath, long LastWriteTime, string EnumFullName), EnumMetadata?> AssemblyFilesCache = [];

    private readonly bool _isFlags;
    private readonly Dictionary<ulong, string> _memberNamesByValue;
    private readonly ImmutableArray<(ulong Value, string Name)> _membersDescending;

    private EnumMetadata(bool isFlags, Dictionary<ulong, string> memberNamesByValue, ImmutableArray<(ulong Value, string Name)> membersDescending)
    {
        _isFlags = isFlags;
        _memberNamesByValue = memberNamesByValue;
        _membersDescending = membersDescending;
    }

    public ImmutableArray<string> GetMemberNames(ulong value)
    {
        if (_memberNamesByValue.TryGetValue(value, out var memberName))
            return [memberName];

        if (!_isFlags)
            return [];

        var remainingValue = value;
        var memberNames = new List<string>();
        foreach (var member in _membersDescending)
        {
            if (member.Value == 0)
                continue;

            if ((remainingValue & member.Value) != member.Value)
                continue;

            memberNames.Add(member.Name);
            remainingValue &= ~member.Value;
        }

        if (remainingValue != 0 || memberNames.Count == 0)
            return [];

        // The members are matched from the largest value to the smallest one, but they are listed from the smallest to the largest one (same as Enum.ToString)
        memberNames.Reverse();
        return [.. memberNames];
    }

    public static EnumMetadata? Create(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
        if (!IsEnum(metadataReader, typeDefinition))
            return null;

        var members = new List<(ulong Value, string Name)>();
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = metadataReader.GetFieldDefinition(fieldHandle);
            if (!field.Attributes.HasFlag(FieldAttributes.Static))
                continue;

            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil)
                continue;

            if (!TryGetValueBits(DecodeIntegralConstant(metadataReader, metadataReader.GetConstant(constantHandle)), out var memberValue))
                continue;

            members.Add((memberValue, metadataReader.GetString(field.Name)));
        }

        if (members.Count == 0)
            return null;

        // Several members can share the same value, so they are ordered by name to always use the same one
        var membersDescending = members
            .OrderByDescending(static member => member.Value)
            .ThenBy(static member => member.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        var memberNamesByValue = new Dictionary<ulong, string>();
        foreach (var member in membersDescending)
        {
            memberNamesByValue.TryAdd(member.Value, member.Name);
        }

        var isFlags = false;
        foreach (var attributeHandle in typeDefinition.GetCustomAttributes())
        {
            if (IsFlagsAttribute(metadataReader, metadataReader.GetCustomAttribute(attributeHandle)))
            {
                isFlags = true;
                break;
            }
        }

        return new EnumMetadata(isFlags, memberNamesByValue, membersDescending);
    }

    // Enums declared in other assemblies are searched in the file of the referenced assembly when it is next to the inspected assembly,
    // then in the assemblies already loaded in the process, then in the directory of the runtime. The files are read, never loaded.
    public static EnumMetadata? FromReferencedAssembly(string? assemblyDirectory, string? assemblyName, string enumFullName)
    {
        // The name comes from metadata, and must not be used to read a file from another directory
        var fileName = string.IsNullOrEmpty(assemblyName) || !string.Equals(Path.GetFileName(assemblyName), assemblyName, StringComparison.Ordinal)
            ? null
            : assemblyName + ".dll";

        EnumMetadata? result = null;
        if (fileName is not null && !string.IsNullOrEmpty(assemblyDirectory))
        {
            result = FromCachedAssemblyFile(Path.Combine(assemblyDirectory, fileName), enumFullName);
        }

        result ??= FromLoadedAssemblies(enumFullName);
        if (result is null && fileName is not null && Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } runtimeDirectory)
        {
            result = FromCachedAssemblyFile(Path.Combine(runtimeDirectory, fileName), enumFullName);
        }

        return result;
    }

    private static EnumMetadata? FromCachedAssemblyFile(string assemblyPath, string enumFullName)
    {
        long lastWriteTime;
        try
        {
            if (!File.Exists(assemblyPath))
                return null;

            lastWriteTime = File.GetLastWriteTimeUtc(assemblyPath).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        var key = (assemblyPath, lastWriteTime, enumFullName);
        lock (LoadedAssembliesCacheLock)
        {
            if (AssemblyFilesCache.TryGetValue(key, out var cachedMetadata))
                return cachedMetadata;
        }

        var result = FromAssemblyFile(assemblyPath, enumFullName);
        lock (LoadedAssembliesCacheLock)
        {
            AssemblyFilesCache[key] = result;
        }

        return result;
    }

    public static EnumMetadata? FromLoadedAssemblies(string enumFullName)
    {
        lock (LoadedAssembliesCacheLock)
        {
            if (LoadedAssembliesCache.TryGetValue(enumFullName, out var cachedMetadata))
                return cachedMetadata;
        }

        EnumMetadata? result = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? assemblyLocation;
            try
            {
                assemblyLocation = assembly.Location;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (string.IsNullOrEmpty(assemblyLocation) || !File.Exists(assemblyLocation))
                continue;

            result = FromAssemblyFile(assemblyLocation, enumFullName);
            if (result is not null)
                break;
        }

        lock (LoadedAssembliesCacheLock)
        {
            LoadedAssembliesCache[enumFullName] = result;
        }

        return result;
    }

    private static EnumMetadata? FromAssemblyFile(string assemblyPath, string enumFullName)
    {
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var metadataReader = peReader.GetMetadataReader();
            foreach (var typeDefinitionHandle in metadataReader.TypeDefinitions)
            {
                if (string.Equals(GetFullName(metadataReader, typeDefinitionHandle), enumFullName, StringComparison.Ordinal))
                    return Create(metadataReader, typeDefinitionHandle);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (BadImageFormatException)
        {
        }

        return null;
    }

    public static bool TryGetUnderlyingType(MetadataReader metadataReader, TypeDefinition typeDefinition, out PrimitiveTypeCode typeCode)
    {
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = metadataReader.GetFieldDefinition(fieldHandle);
            if (!string.Equals(metadataReader.GetString(field.Name), "value__", StringComparison.Ordinal))
                continue;

            var signature = metadataReader.GetBlobBytes(field.Signature);
            if (signature.Length < 2 || signature[0] != 0x06)
                break;

            PrimitiveTypeCode? result = signature[1] switch
            {
                0x02 => PrimitiveTypeCode.Boolean,
                0x03 => PrimitiveTypeCode.Char,
                0x04 => PrimitiveTypeCode.SByte,
                0x05 => PrimitiveTypeCode.Byte,
                0x06 => PrimitiveTypeCode.Int16,
                0x07 => PrimitiveTypeCode.UInt16,
                0x08 => PrimitiveTypeCode.Int32,
                0x09 => PrimitiveTypeCode.UInt32,
                0x0A => PrimitiveTypeCode.Int64,
                0x0B => PrimitiveTypeCode.UInt64,
                _ => null,
            };

            typeCode = result ?? PrimitiveTypeCode.Int32;
            return result is not null;
        }

        typeCode = PrimitiveTypeCode.Int32;
        return false;
    }

    public static bool TryGetValueBits(object? value, out ulong bits)
    {
        switch (value)
        {
            case sbyte sbyteValue:
                bits = unchecked((ulong)sbyteValue);
                return true;
            case byte byteValue:
                bits = byteValue;
                return true;
            case short shortValue:
                bits = unchecked((ulong)shortValue);
                return true;
            case ushort ushortValue:
                bits = ushortValue;
                return true;
            case int intValue:
                bits = unchecked((ulong)intValue);
                return true;
            case uint uintValue:
                bits = uintValue;
                return true;
            case long longValue:
                bits = unchecked((ulong)longValue);
                return true;
            case ulong ulongValue:
                bits = ulongValue;
                return true;
            case char charValue:
                bits = charValue;
                return true;
            case bool boolValue:
                bits = boolValue ? 1UL : 0UL;
                return true;
            default:
                bits = 0;
                return false;
        }
    }

    private static object? DecodeIntegralConstant(MetadataReader metadataReader, Constant constant)
    {
        var span = metadataReader.GetBlobBytes(constant.Value).AsSpan();
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean when span.Length >= 1 => span[0] != 0,
            ConstantTypeCode.Char when span.Length >= 2 => (char)BinaryPrimitives.ReadUInt16LittleEndian(span),
            ConstantTypeCode.SByte when span.Length >= 1 => unchecked((sbyte)span[0]),
            ConstantTypeCode.Byte when span.Length >= 1 => span[0],
            ConstantTypeCode.Int16 when span.Length >= 2 => BinaryPrimitives.ReadInt16LittleEndian(span),
            ConstantTypeCode.UInt16 when span.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            ConstantTypeCode.Int32 when span.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(span),
            ConstantTypeCode.UInt32 when span.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            ConstantTypeCode.Int64 when span.Length >= 8 => BinaryPrimitives.ReadInt64LittleEndian(span),
            ConstantTypeCode.UInt64 when span.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(span),
            _ => null,
        };
    }

    private static bool IsEnum(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        var baseType = typeDefinition.BaseType;
        return baseType.Kind switch
        {
            HandleKind.TypeReference => metadataReader.GetTypeReference((TypeReferenceHandle)baseType) is var typeReference &&
                                        metadataReader.StringComparer.Equals(typeReference.Namespace, "System") &&
                                        metadataReader.StringComparer.Equals(typeReference.Name, "Enum"),
            HandleKind.TypeDefinition => metadataReader.GetTypeDefinition((TypeDefinitionHandle)baseType) is var baseTypeDefinition &&
                                         metadataReader.StringComparer.Equals(baseTypeDefinition.Namespace, "System") &&
                                         metadataReader.StringComparer.Equals(baseTypeDefinition.Name, "Enum"),
            _ => false,
        };
    }

    private static bool IsFlagsAttribute(MetadataReader metadataReader, CustomAttribute attribute)
    {
        EntityHandle typeHandle = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => metadataReader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

        return typeHandle.Kind switch
        {
            HandleKind.TypeReference => metadataReader.GetTypeReference((TypeReferenceHandle)typeHandle) is var typeReference &&
                                        metadataReader.StringComparer.Equals(typeReference.Namespace, "System") &&
                                        metadataReader.StringComparer.Equals(typeReference.Name, "FlagsAttribute"),
            HandleKind.TypeDefinition => metadataReader.GetTypeDefinition((TypeDefinitionHandle)typeHandle) is var typeDefinition &&
                                         metadataReader.StringComparer.Equals(typeDefinition.Namespace, "System") &&
                                         metadataReader.StringComparer.Equals(typeDefinition.Name, "FlagsAttribute"),
            _ => false,
        };
    }

    private static string GetFullName(MetadataReader metadataReader, TypeDefinitionHandle handle)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(handle);
        var name = metadataReader.GetString(typeDefinition.Name);
        var declaringTypeHandle = typeDefinition.GetDeclaringType();
        if (!declaringTypeHandle.IsNil)
            return GetFullName(metadataReader, declaringTypeHandle) + "+" + name;

        var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : metadataReader.GetString(typeDefinition.Namespace);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }
}
