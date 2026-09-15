using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <param name="FilePath">The source file containing the call to update.</param>
/// <param name="LineNumber">The line of the method name of the call, as reported by <see cref="CallerLineNumberAttribute"/>.</param>
/// <param name="SequencePointLineNumber">The line of the statement containing the call, as reported by the PDB, or 0 when unknown.</param>
/// <param name="SequencePointColumnNumber">The 1-based column of the statement containing the call, as reported by the PDB, or 0 when unknown.</param>
/// <param name="MethodName">The name of the method decorated with <see cref="InlineSnapshotAssertionAttribute"/>.</param>
/// <param name="ParameterName">The name of the parameter holding the snapshot.</param>
/// <param name="ParameterIndex">The index of the parameter holding the snapshot in the method declaration.</param>
/// <param name="ParameterDefaultValue">The default value of the parameter holding the snapshot, which is the snapshot when the argument is omitted.</param>
/// <param name="IsExtensionMethod">Whether the method is an extension method, whose first parameter is the receiver when called with the extension syntax.</param>
/// <param name="DeclaringTypeName">The name of the type declaring the method.</param>
/// <param name="CallerMemberName">The name of the member, as written in source, containing the call to update, or null when it cannot be named.</param>
/// <param name="AssemblyLocation">The assembly whose PDB describes how the source file was compiled.</param>
internal readonly record struct CallerContext(
    FullPath FilePath,
    int LineNumber,
    int SequencePointLineNumber,
    int SequencePointColumnNumber,
    string MethodName,
    string ParameterName,
    int ParameterIndex,
    string? ParameterDefaultValue,
    bool IsExtensionMethod,
    string? DeclaringTypeName,
    string? CallerMemberName,
    string? AssemblyLocation)
{
    private static readonly Guid CompilationOptionsGuid = new(0xB5FEEC05, 0x8CD0, 0x4A83, 0x96, 0xDA, 0x46, 0x62, 0x84, 0xBB, 0x4B, 0xD8) /* B5FEEC05-8CD0-4A83-96DA-466284BB4BD8 */;
    private static readonly ConcurrentDictionary<string, PdbCompilationOptions> CompilationOptionsCache = new(StringComparer.Ordinal);

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static CallerContext Get(InlineSnapshotSettings settings, string? filePath, int lineNumber)
    {
        MethodBase? attributedMethod = null;
        InlineSnapshotAssertionAttribute? attribute = null;
        StackFrame? attributedFrame = null;
        StackFrame? callerFrame = null;

        var stackTrace = new StackTrace(fNeedFileInfo: true);
        for (var i = stackTrace.FrameCount - 1; i >= 0; i--)
        {
            var frame = stackTrace.GetFrame(i);
            if (frame is null)
                continue;

            var method = frame.GetMethod();
            if (method is null)
                continue;

            method = CallerContextUtilities.ResolveActualMethod(method);

            attribute = method.GetCustomAttribute<InlineSnapshotAssertionAttribute>();
            if (attribute is null)
                continue;

            attributedMethod = method;
            attributedFrame = frame;
            callerFrame = stackTrace.GetFrame(i + 1);
            break;
        }

        if (attributedMethod is null || attribute is null || callerFrame is null)
            throw new InlineSnapshotException($"Cannot find the method to update in the call stack. Be sure at least one method from the stack is decorated with '{nameof(InlineSnapshotAssertionAttribute)}'.");

        var methodName = attributedMethod.Name;
        if (CallerContextUtilities.TryParseLocalFunctionName(methodName, out var localFunctionName))
        {
            methodName = localFunctionName;
        }

        var parameterName = attribute.ParameterName;
        var parameters = attributedMethod.GetParameters();
        var parameterIndex = Array.FindIndex(parameters, parameter => parameter.Name == parameterName);

        // Falling through with an unknown index would let the file editor pick an unrelated argument.
        if (parameterIndex < 0)
            throw new InlineSnapshotException($"'{attributedMethod.DeclaringType?.FullName}.{attributedMethod.Name}' is decorated with '{nameof(InlineSnapshotAssertionAttribute)}' referencing the parameter '{parameterName}', but the method has no such parameter.");

        var parameterDefaultValue = parameters[parameterIndex].HasDefaultValue ? parameters[parameterIndex].DefaultValue as string : null;

        var pdbFileName = callerFrame.GetFileName();
        var stackTraceFilePath = CallerContextUtilities.ResolveSourceFilePath(pdbFileName, fallbackToOriginalPath: true);
        var callerFilePath = CallerContextUtilities.ResolveSourceFilePath(filePath, fallbackToOriginalPath: true);
        var pdbLine = callerFrame.GetFileLineNumber();

        // A helper that does not forward [CallerFilePath] and [CallerLineNumber] reports its own location, which is
        // the call it makes inside its body rather than the call to update.
        if (attributedFrame is not null && pdbFileName is not null && callerFilePath is not null &&
            CallerContextUtilities.ResolveSourceFilePath(attributedFrame.GetFileName(), fallbackToOriginalPath: true) == callerFilePath &&
            attributedFrame.GetFileLineNumber() == lineNumber &&
            (stackTraceFilePath != callerFilePath || pdbLine != lineNumber))
        {
            throw new InlineSnapshotException($"""
                The location of the snapshot points inside '{attributedMethod.DeclaringType?.FullName}.{attributedMethod.Name}' instead of its caller.
                Add [CallerFilePath] and [CallerLineNumber] parameters to this method and forward them to the validation method it calls.
                """);
        }

        if (settings.ValidateSourceFilePathUsingPdbInfoWhenAvailable && stackTraceFilePath is not null && callerFilePath is not null && stackTraceFilePath != callerFilePath)
        {
            throw new InlineSnapshotException($"""
                The call stack doesn't match the file to update. This may happen when you build the project in Release configuration.
                You can disable the validation using {nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.ValidateSourceFilePathUsingPdbInfoWhenAvailable)} = false.
                From call stack: {pdbFileName}; From CallerFilePath: {filePath}
                """);
        }

        if (settings.ValidateLineNumberUsingPdbInfoWhenAvailable && pdbLine != 0 && pdbLine != lineNumber)
        {
            throw new InlineSnapshotException($""""
                The call stack does not match the line to update. This may happen when you build the project in Release configuration.
                You can disable the validation using {nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.ValidateLineNumberUsingPdbInfoWhenAvailable)} = false.
                From call stack: {pdbLine}; From CallerLineNumber: {lineNumber}.
                """");
        }

        var resolvedFilePath = callerFilePath ?? stackTraceFilePath;
        if (resolvedFilePath is null)
            throw new InlineSnapshotException("Cannot find the file to update from the call stack. The PDB may be missing.");

        // The PDB backs both the language-version filtering and the preprocessor symbols used to parse the file,
        // so the location must be captured whatever the allowed string formats are.
        string? assemblyLocation;
        int sequencePointLine;
        int sequencePointColumn;
        if (pdbFileName is not null)
        {
            assemblyLocation = callerFrame.GetMethod()?.DeclaringType?.Assembly.Location;
            sequencePointLine = pdbLine;
            sequencePointColumn = callerFrame.GetFileColumnNumber();
        }
        else
        {
            // The caller frame has no source information, typically because an async helper resumed after an await on
            // a thread-pool thread: the frame above it belongs to the runtime, not to the code calling the helper.
            // The helper is usually compiled with the code calling it, so its assembly is the best remaining source of
            // compilation options.
            assemblyLocation = attributedMethod.DeclaringType?.Assembly.Location;
            sequencePointLine = 0;
            sequencePointColumn = 0;
        }

        return new CallerContext(
            resolvedFilePath.Value,
            lineNumber,
            sequencePointLine,
            sequencePointColumn,
            methodName,
            parameterName,
            parameterIndex,
            parameterDefaultValue,
            attributedMethod.IsDefined(typeof(ExtensionAttribute), inherit: false),
            attributedMethod.DeclaringType?.Name,
            GetSourceMemberName(callerFrame.GetMethod()),
            string.IsNullOrEmpty(assemblyLocation) ? null : assemblyLocation);
    }

    /// <summary>
    /// Returns the name of the member as written in source. Lambdas, local functions and state machines are compiled
    /// to members named after the member declaring them (for example <c>&lt;Test&gt;b__0_0</c>). Constructors and
    /// top-level statements have no name to look for.
    /// </summary>
    internal static string? GetSourceMemberName(MethodBase? method)
    {
        if (method is null)
            return null;

        method = CallerContextUtilities.ResolveActualMethod(method);
        var name = method.Name;
        if (name.StartsWith('<', StringComparison.Ordinal))
        {
            var end = name.IndexOf('>', StringComparison.Ordinal);
            if (end <= 1)
                return null;

            name = name[1..end];
        }

        return name is ".ctor" or ".cctor" or "Main" || name.Contains('<', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal) ? null : name;
    }

    public string[]? GetCompilationDefines() => GetCompilationOptions().Defines;

    public CSharpStringFormats FilterFormats(CSharpStringFormats formats)
    {
        if (!formats.HasFlag(CSharpStringFormats.DetermineFeatureFromPdb))
            return formats;

        var languageVersion = GetCompilationOptions().LanguageVersion;
        if (languageVersion is not null && languageVersion.Major < 11)
        {
            formats &= ~(CSharpStringFormats.LeftAlignedRaw | CSharpStringFormats.Raw);
        }

        return formats;
    }

    private PdbCompilationOptions GetCompilationOptions()
    {
        if (AssemblyLocation is null)
            return PdbCompilationOptions.Unknown;

        return CompilationOptionsCache.GetOrAdd(AssemblyLocation, ReadCompilationOptions);
    }

    private static PdbCompilationOptions ReadCompilationOptions(string assemblyLocation)
    {
        try
        {
            using var stream = File.OpenRead(assemblyLocation);
            using var reader = new PEReader(stream);
            if (!reader.TryOpenAssociatedPortablePdb(assemblyLocation, File.OpenRead, out var metadataReaderProvider, out _) || metadataReaderProvider is null)
            {
                metadataReaderProvider?.Dispose();
                return PdbCompilationOptions.Unknown;
            }

            using (metadataReaderProvider)
            {
                var metadataReader = metadataReaderProvider.GetMetadataReader();
                foreach (var handle in metadataReader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
                {
                    var customDebugInformation = metadataReader.GetCustomDebugInformation(handle);
                    if (metadataReader.GetGuid(customDebugInformation.Kind) != CompilationOptionsGuid)
                        continue;

                    Version? languageVersion = null;
                    string[]? defines = null;

                    // Compiler flag bytes are UTF-8 null-terminated key-value pairs
                    var blobReader = metadataReader.GetBlobReader(customDebugInformation.Value);
                    var nullIndex = blobReader.IndexOf(0);
                    while (nullIndex >= 0)
                    {
                        var key = blobReader.ReadUTF8(nullIndex);
                        blobReader.ReadByte();

                        nullIndex = blobReader.IndexOf(0);
                        if (nullIndex < 0)
                            break;

                        var value = blobReader.ReadUTF8(nullIndex);
                        blobReader.ReadByte();

                        nullIndex = blobReader.IndexOf(0);
                        switch (key)
                        {
                            case "define":
                                defines = value.Split(',');
                                break;

                            case "language-version":
                                // "preview" has no exact version, but it is at least 12.0 as the minimum supported TFM is .NET 8, which requires C# 12.
                                languageVersion = Version.TryParse(value, out var version) ? version : value is "preview" ? new Version(12, 0) : null;
                                break;
                        }
                    }

                    return new PdbCompilationOptions(languageVersion, defines);
                }
            }
        }
        catch
        {
        }

        return PdbCompilationOptions.Unknown;
    }

    private sealed record PdbCompilationOptions(Version? LanguageVersion, string[]? Defines)
    {
        public static PdbCompilationOptions Unknown { get; } = new(LanguageVersion: null, Defines: null);
    }
}
