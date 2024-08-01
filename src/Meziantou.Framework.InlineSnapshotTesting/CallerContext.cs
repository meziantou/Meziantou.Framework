using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal record struct CallerContext(string FilePath, int LineNumber, int ColumnNumber, string MethodName, string? ParameterName, int ParameterIndex, string? AssemblyLocation)
{
    private static readonly ConcurrentDictionary<string, Version?> LanguageVersionCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Newer Roslyn versions use the format "&lt;callerName&gt;g__functionName|x_y".
    /// Older versions use "&lt;callerName&gt;g__functionNamex_y".
    /// </summary>
    /// <see href="https://github.com/dotnet/roslyn/blob/aecd49800750d64e08767836e2678ffa62a4647f/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs#L109" />
    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout")]
    private static readonly Regex FunctionNameRegex = new(@"^<(.*)>g__(?<name>[^\|]*)\|{0,1}[0-9]+(_[0-9]+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static CallerContext Get(InlineSnapshotSettings settings, string? filePath, int lineNumber)
    {
        string? methodName = null;
        string? parameterName = null;
        var parameterIndex = -1;

        var stackTrace = new StackTrace(fNeedFileInfo: true);
        StackFrame? callerFrame = null;
        for (var i = stackTrace.FrameCount - 1; i >= 0; i--)
        {
            var frame = stackTrace.GetFrame(i);
            if (frame is null)
                continue;

            var method = frame.GetMethod();
            if (method == null)
                continue;

            method = GetActualMethod(method);

            var attribute = method.GetCustomAttribute<InlineSnapshotAssertionAttribute>();
            if (attribute is null)
                continue;

            methodName = method.Name;
            if (ParseLocalFunctionName(methodName, out var localFunctionName))
            {
                methodName = localFunctionName;
            }

            parameterName = attribute.ParameterName;
            if (parameterName is not null)
            {
                var parameters = method.GetParameters();
                for (var j = 0; j < parameterName.Length; j++)
                {
                    if (parameters[j].Name == parameterName)
                    {
                        parameterIndex = j;
                        break;
                    }
                }
            }

            callerFrame = stackTrace.GetFrame(i + 1);
            break;
        }

        if (callerFrame is null)
            throw new InlineSnapshotException($"Cannot find the method to update in the call stack. Be sure at least one method from the stack is decorated with '{nameof(InlineSnapshotAssertionAttribute)}'.");

        var pdbFileName = callerFrame.GetFileName();
        if (settings.ValidateSourceFilePathUsingPdbInfoWhenAvailable && pdbFileName is not null && filePath is not null && pdbFileName != filePath)
        {
            throw new InlineSnapshotException($"""
                The call stack doesn't match the file to update. This may happen when you build the project in Release configuration.
                You can disable the validation using {nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.ValidateSourceFilePathUsingPdbInfoWhenAvailable)} = false.
                From call stack: {pdbFileName}; From CallerFilePath: {filePath}
                """);
        }

        var pdbLine = callerFrame.GetFileLineNumber();
        if (settings.ValidateLineNumberUsingPdbInfoWhenAvailable && pdbLine != 0 && pdbLine != lineNumber)
        {
            throw new InlineSnapshotException($""""
                The call stack does not match the line to update. This may happen when you build the project in Release configuration.
                You can disable the validation using {nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.ValidateLineNumberUsingPdbInfoWhenAvailable)} = false.
                From call stack: {pdbLine}; From CallerLineNumber: {lineNumber}.
                """");
        }

        filePath ??= pdbFileName;
        var column = callerFrame.GetFileColumnNumber();

        if (filePath is null)
            throw new InlineSnapshotException("Cannot find the file to update from the call stack. The PDB may be missing.");

        if (methodName is null)
            throw new InlineSnapshotException("Cannot find the method to update from the call stack. The code may be optimized (Release configuration).");

        string? assemblyLocation = null;
        if (settings.AllowedStringFormats.HasFlag(CSharpStringFormats.DetermineFeatureFromPdb))
        {
            // Read the language version from the PDB
            assemblyLocation = callerFrame.GetMethod()?.DeclaringType?.Assembly?.Location;
        }

        return new CallerContext(filePath, lineNumber, column, methodName, parameterName, parameterIndex, assemblyLocation);
    }

    public readonly string[]? GetCompilationDefines()
    {
        try
        {
            using var stream = File.OpenRead(AssemblyLocation);
            using var reader = new PEReader(stream);
            if (!reader.TryOpenAssociatedPortablePdb(AssemblyLocation, File.OpenRead, out var metadataReaderProvider, out _) || metadataReaderProvider is null)
            {
                metadataReaderProvider?.Dispose();
                return null;
            }

            using (metadataReaderProvider)
            {
                var metadataReader = metadataReaderProvider.GetMetadataReader();
                foreach (var handle in metadataReader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
                {
                    var customDebugInformation = metadataReader.GetCustomDebugInformation(handle);
                    var compilationOptionsGuid = new Guid("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");
                    if (metadataReader.GetGuid(customDebugInformation.Kind) == compilationOptionsGuid)
                    {
                        var blobReader = metadataReader.GetBlobReader(customDebugInformation.Value);

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

                            if (key == "define")
                            {
                                return value.Split(',');
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public readonly CSharpStringFormats FilterFormats(CSharpStringFormats formats)
    {
        if (AssemblyLocation is null || !formats.HasFlag(CSharpStringFormats.DetermineFeatureFromPdb))
            return formats;

        var languageVersion = LanguageVersionCache.GetOrAdd(AssemblyLocation, GetCSharpLanguageVersionFromAssemblyLocation);
        if (languageVersion != null && languageVersion.Major < 11)
        {
            formats &= ~(CSharpStringFormats.LeftAlignedRaw | CSharpStringFormats.Raw);
        }

        return formats;
    }

    private static MethodBase GetActualMethod(MethodBase method)
    {
        if (method.DeclaringType.IsAssignableTo(typeof(IAsyncStateMachine)))
        {
            var parentType = method.DeclaringType.DeclaringType;
            if (parentType is not null)
            {
                static MethodInfo[] GetDeclaredMethods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                var methods = GetDeclaredMethods(parentType);
                if (methods is not null)
                {
                    foreach (var candidateMethod in methods)
                    {
                        var attributes = candidateMethod.GetCustomAttributes<StateMachineAttribute>(inherit: false);
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalse - Taken from CoreFX
                        if (attributes is null)
                        {
                            continue;
                        }

                        bool foundAttribute = false, foundIteratorAttribute = false;
                        foreach (var asma in attributes)
                        {
                            if (asma.StateMachineType == method.DeclaringType)
                            {
                                foundAttribute = true;
                                foundIteratorAttribute |= asma is IteratorStateMachineAttribute
                                    || typeof(System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute) != null
                                    && typeof(System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute).IsInstanceOfType(asma);
                            }
                        }

                        if (foundAttribute)
                        {
                            // If this is an iterator (sync or async), mark the iterator as changed, so it gets the + annotation
                            // of the original method. Non-iterator async state machines resolve directly to their builder methods
                            // so aren't marked as changed.
                            return candidateMethod;
                        }
                    }
                }
            }
        }

        return method;
    }

    private static Version? GetCSharpLanguageVersionFromAssemblyLocation(string assemblyLocation)
    {
        try
        {
            using var stream = File.OpenRead(assemblyLocation);
            using var reader = new PEReader(stream);
            if (!reader.TryOpenAssociatedPortablePdb(assemblyLocation, File.OpenRead, out var metadataReaderProvider, out _) || metadataReaderProvider is null)
            {
                metadataReaderProvider?.Dispose();
                return null;
            }

            using (metadataReaderProvider)
            {
                var metadataReader = metadataReaderProvider.GetMetadataReader();
                foreach (var handle in metadataReader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
                {
                    var customDebugInformation = metadataReader.GetCustomDebugInformation(handle);
                    var compilationOptionsGuid = new Guid("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8");
                    if (metadataReader.GetGuid(customDebugInformation.Kind) == compilationOptionsGuid)
                    {
                        var blobReader = metadataReader.GetBlobReader(customDebugInformation.Value);

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

                            if (key == "language-version")
                            {
                                if (Version.TryParse(value, out var version))
                                    return version;

                                return default;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    internal static bool ParseLocalFunctionName(string name, [NotNullWhen(true)] out string? functionName)
    {
        functionName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = FunctionNameRegex.Match(name);
        functionName = match.Groups["name"].Value;
        return match.Success;
    }
}

