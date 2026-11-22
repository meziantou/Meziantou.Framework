using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Meziantou.Framework.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

/// <summary>
/// Generates a class per module of the <c>AdditionalFiles</c> items whose extension is <c>.wasm</c> or <c>.wat</c>: a
/// strongly typed method per exported function, and a property per exported memory, table, global and tag.
/// </summary>
[Generator]
public sealed class WasmModuleGenerator : IIncrementalGenerator
{
    internal const string OutputsTrackingName = "WasmModuleOutputs";

    // The members of the generated class, and of object
    private static readonly string[] ReservedNames = ["Instance", "Compile", "Instantiate", "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize", "ReferenceEquals"];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var outputs = context.AdditionalTextsProvider
            .Where(static text => IsModuleFile(text.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateOutput(pair.Left, pair.Right, cancellationToken))
            .WithTrackingName(OutputsTrackingName);

        context.RegisterSourceOutput(outputs.Collect(), static (context, outputs) =>
        {
            var classNames = new Dictionary<string, ModuleOutput>(StringComparer.Ordinal);
            foreach (var output in outputs.OrderBy(output => output.FilePath, StringComparer.Ordinal))
            {
                foreach (var diagnostic in output.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic.ToDiagnostic(trees: null));
                }

                if (output.FullClassName is null || output.Source is null)
                    continue;

                if (classNames.TryGetValue(output.FullClassName, out var existing))
                {
                    context.ReportDiagnostic(Diagnostic.Create(WasmDiagnostics.DuplicateClassName, Location.None, output.FilePath, output.FullClassName, existing.FilePath));
                    continue;
                }

                classNames.Add(output.FullClassName, output);
                context.AddSource(output.HintName, output.Source);
            }
        });
    }

    private static bool IsModuleFile(string path) => path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wat", StringComparison.OrdinalIgnoreCase);

    private static ModuleOutput CreateOutput(AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider, CancellationToken cancellationToken)
    {
        var path = file.Path;
        var options = optionsProvider.GetOptions(file);
        var isText = path.EndsWith(".wat", StringComparison.OrdinalIgnoreCase);
        var fileName = Path.GetFileNameWithoutExtension(path);

        var className = GetOption(options, "build_metadata.AdditionalFiles.WasmClassName");
        if (className is null)
        {
            className = CSharpNaming.ToPascalCase(fileName, "Wasm");
            if (!className.EndsWith("Module", StringComparison.Ordinal))
            {
                className += "Module";
            }
        }
        else if (!CSharpNaming.IsValidIdentifier(className))
        {
            return ModuleOutput.Failed(path, DiagnosticInfo.Create(WasmDiagnostics.InvalidName, Location.None, "class name", className, path));
        }

        var namespaceName = GetOption(options, "build_metadata.AdditionalFiles.WasmNamespace") ?? GetOption(options, "build_property.RootNamespace");
        if (namespaceName is not null && !namespaceName.Split('.').All(CSharpNaming.IsValidIdentifier))
            return ModuleOutput.Failed(path, DiagnosticInfo.Create(WasmDiagnostics.InvalidName, Location.None, "namespace", namespaceName, path));

        var visibility = string.Equals(GetOption(options, "build_metadata.AdditionalFiles.WasmVisibility"), "public", StringComparison.OrdinalIgnoreCase) ? "public" : "internal";
        var embed = !string.Equals(GetOption(options, "build_metadata.AdditionalFiles.WasmEmbed"), "false", StringComparison.OrdinalIgnoreCase);

        string? text = null;
        byte[]? bytes = null;
        WasmModuleDescription description;
        try
        {
            if (isText)
            {
                text = file.GetText(cancellationToken)?.ToString();
                if (text is null)
                    return ModuleOutput.Failed(path, DiagnosticInfo.Create(WasmDiagnostics.CannotReadModule, Location.None, path, "the file is not available"));

                description = WatDescriptionReader.Read(text);
            }
            else
            {
                // An additional file is read as text: the bytes of a binary module are read from the file
#pragma warning disable RS1035 // The bytes of the module are not available from the compilation
                bytes = File.ReadAllBytes(path);
#pragma warning restore RS1035
                description = WasmBinaryDescriptionReader.Read(bytes);
            }
        }
        catch (WasmDescriptionException ex)
        {
            return ModuleOutput.Failed(path, DiagnosticInfo.Create(WasmDiagnostics.InvalidModule, GetLocation(path, text, ex.Position), path, ex.Reason));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleOutput.Failed(path, DiagnosticInfo.Create(WasmDiagnostics.CannotReadModule, Location.None, path, ex.Message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var source = Emit(description, Path.GetFileName(path), namespaceName, className, visibility, embed ? text : null, embed ? bytes : null);
        var fullClassName = namespaceName is null ? className : namespaceName + "." + className;
        return new ModuleOutput(path, fullClassName, fullClassName + "." + StableHash.Compute(fullClassName) + ".WasmModule.g.cs", source, ImmutableEquatableArray<DiagnosticInfo>.Empty);
    }

    private static string? GetOption(AnalyzerConfigOptions options, string name)
        => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static LocationInfo? GetLocation(string path, string? text, int position)
    {
        if (text is null)
            return null;

        var sourceText = SourceText.From(text);
        var span = new TextSpan(Math.Min(Math.Max(position, 0), text.Length), 0);
        return new LocationInfo(path, span, sourceText.Lines.GetLinePositionSpan(span));
    }

    private static string Emit(WasmModuleDescription description, string fileName, string? namespaceName, string className, string visibility, string? text, byte[]? bytes)
    {
        const string Ns = WasmCode.Namespace;
        var writer = new CodeWriter();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        if (namespaceName is not null)
        {
            writer.WriteLine("namespace " + namespaceName);
            writer.OpenBlock();
        }

        var members = CreateMembers(description, className);
        var bufferSizes = new SortedSet<int>();
        foreach (var member in members)
        {
            if (member.Export.Function is { } function)
            {
                if (function.Parameters.Count > 1)
                {
                    bufferSizes.Add(function.Parameters.Count);
                }

                if (function.Results.Count > 1)
                {
                    bufferSizes.Add(function.Results.Count);
                }
            }
        }

        var fileText = WasmCode.XmlText(fileName);
        writer.WriteLine("/// <summary>The exports of the WebAssembly module <c>" + fileText + "</c>: wraps an instance of the module.</summary>");
        writer.WriteLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Meziantou.Framework.WasmEngine.SourceGenerator\", \"1.0.0\")]");
        writer.WriteLine(visibility + " sealed partial class " + className);
        writer.OpenBlock();

        foreach (var member in members.Where(member => member.Export.Kind == WasmExportKind.Function))
        {
            writer.WriteLine("private readonly " + Ns + "WasmFunction " + member.FieldName + ";");
        }

        writer.WriteLine();
        writer.WriteLine("/// <summary>Wraps an instance of the module.</summary>");
        writer.WriteLine("/// <param name=\"instance\">An instance of the module.</param>");
        writer.WriteLine("/// <exception cref=\"" + Ns + "WasmLinkException\">An export of the module is missing, or has another type.</exception>");
        writer.WriteLine("public " + className + "(" + Ns + "WasmInstance instance)");
        writer.OpenBlock();
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(instance);");
        writer.WriteLine("Instance = instance;");
        foreach (var member in members)
        {
            var name = WasmCode.Literal(member.Export.Name);
            if (member.Export.Function is { } function)
            {
                writer.WriteLine(member.FieldName + " = __GetFunction(instance, " + name + ", " + WasmCode.GetValueTypesExpression(function.Parameters) + ", " + WasmCode.GetValueTypesExpression(function.Results) + ");");
            }
            else
            {
                writer.WriteLine(member.Name + " = __GetExport<" + Ns + GetExternTypeName(member.Export.Kind) + ">(instance, " + name + ", \"" + GetKindName(member.Export.Kind) + "\");");
            }
        }

        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("/// <summary>Gets the instance of the module.</summary>");
        writer.WriteLine("public " + Ns + "WasmInstance Instance { get; }");

        if (text is not null || bytes is not null)
        {
            writer.WriteLine();
            writer.WriteLine("/// <summary>Compiles the module <c>" + fileText + "</c>.</summary>");
            writer.WriteLine("/// <param name=\"runtime\">The runtime that compiles the module.</param>");
            writer.WriteLine("/// <returns>The compiled module.</returns>");
            writer.WriteLine("public static " + Ns + "WasmModule Compile(" + Ns + "WasmRuntime runtime)");
            writer.OpenBlock();
            writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(runtime);");
            writer.WriteLine(text is not null ? "return runtime.Compile(" + Ns + "WasmRuntime.WatToWasm(" + WasmCode.Literal(text) + "));" : "return runtime.Compile(__ModuleBytes);");
            writer.CloseBlock();

            if (bytes is not null)
            {
                writer.WriteLine();
                writer.WriteLine("private static global::System.ReadOnlySpan<byte> __ModuleBytes => " + FormatBytes(bytes) + ";");
            }
        }

        writer.WriteLine();
        writer.WriteLine("/// <summary>Instantiates a module in a store, and wraps the instance.</summary>");
        writer.WriteLine("/// <param name=\"store\">The store of the instance.</param>");
        writer.WriteLine("/// <param name=\"module\">The module.</param>");
        writer.WriteLine("/// <param name=\"imports\">The values of the imports of the module.</param>");
        writer.WriteLine("/// <returns>The wrapped instance.</returns>");
        writer.WriteLine("public static " + className + " Instantiate(" + Ns + "WasmStore store, " + Ns + "WasmModule module, " + Ns + "WasmImports? imports = null)");
        writer.OpenBlock();
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(store);");
        writer.WriteLine("return new " + className + "(store.Instantiate(module, imports ?? new " + Ns + "WasmImports()));");
        writer.CloseBlock();

        foreach (var member in members)
        {
            writer.WriteLine();
            if (member.Export.Function is { } function)
            {
                WriteFunction(writer, member, function);
            }
            else
            {
                writer.WriteLine("/// <summary>Gets the exported " + GetKindName(member.Export.Kind) + " <c>" + WasmCode.XmlText(member.Export.Name) + "</c>.</summary>");
                writer.WriteLine("public " + Ns + GetExternTypeName(member.Export.Kind) + " " + member.Name + " { get; }");
            }
        }

        writer.WriteLine();
        writer.WriteLine("private static " + Ns + "WasmFunction __GetFunction(" + Ns + "WasmInstance instance, string name, " + Ns + "WasmValueType[] parameters, " + Ns + "WasmValueType[] results)");
        writer.OpenBlock();
        writer.WriteLine("var function = instance.GetFunction(name) ?? throw new " + Ns + "WasmLinkException(\"The instance has no exported function '\" + name + \"'\");");
        writer.WriteLine("var type = function.Type;");
        writer.WriteLine("if (!global::System.MemoryExtensions.SequenceEqual<" + Ns + "WasmValueType>(type.Parameters, parameters) || !global::System.MemoryExtensions.SequenceEqual<" + Ns + "WasmValueType>(type.Results, results))");
        writer.WriteLine("    throw new " + Ns + "WasmLinkException(\"The exported function '\" + name + \"' does not have the type of the module of the generated class\");");
        writer.WriteLine();
        writer.WriteLine("return function;");
        writer.CloseBlock();

        writer.WriteLine();
        writer.WriteLine("private static T __GetExport<T>(" + Ns + "WasmInstance instance, string name, string kind)");
        writer.WriteLine("    where T : " + Ns + "WasmExtern");
        writer.OpenBlock();
        writer.WriteLine("return instance.GetExport(name) as T ?? throw new " + Ns + "WasmLinkException(\"The instance has no exported \" + kind + \" '\" + name + \"'\");");
        writer.CloseBlock();

        if (members.Any(member => member.Export.Function is { } function && function.Results.Any(result => WasmCode.GetMarshalling(result) == ValueMarshalling.HostObject)))
        {
            writer.WriteLine();
            WasmCode.WriteHostValueHelper(writer);
        }

        foreach (var size in bufferSizes)
        {
            writer.WriteLine();
            writer.WriteLine("[global::System.Runtime.CompilerServices.InlineArray(" + size.ToString(CultureInfo.InvariantCulture) + ")]");
            writer.WriteLine("private struct __WasmValues" + size.ToString(CultureInfo.InvariantCulture));
            writer.OpenBlock();
            writer.WriteLine("private " + WasmCode.WasmValue + " _element0;");
            writer.CloseBlock();
        }

        writer.CloseBlock();
        if (namespaceName is not null)
        {
            writer.CloseBlock();
        }

        return writer.ToString();
    }

    private static void WriteFunction(CodeWriter writer, MemberModel member, WasmFunctionSignature function)
    {
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new List<(string Name, ValueMarshalling Marshalling)>();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var fallback = "arg" + i.ToString(CultureInfo.InvariantCulture);
            var name = function.ParameterNames[i] is { } parameterName ? CSharpNaming.ToCamelCase(parameterName, fallback) : fallback;
            parameters.Add((CSharpNaming.MakeUnique(name, parameterNames), WasmCode.GetMarshalling(function.Parameters[i])));
        }

        var resultTypes = function.Results.Select(result => WasmCode.GetClrType(WasmCode.GetMarshalling(result), result.Nullable)).ToList();
        writer.WriteLine("/// <summary>Calls the exported function <c>" + WasmCode.XmlText(member.Export.Name) + "</c>: <c>" + WasmCode.XmlText(function.ToString()) + "</c>.</summary>");
        var signature = string.Join(", ", parameters.Select((parameter, index) => WasmCode.GetClrType(parameter.Marshalling, function.Parameters[index].Nullable) + " " + parameter.Name));
        writer.WriteLine("public " + WasmCode.GetReturnType(resultTypes) + " " + member.Name + "(" + signature + ")");
        writer.OpenBlock();

        string argumentsExpression;
        switch (parameters.Count)
        {
            case 0:
                argumentsExpression = "global::System.ReadOnlySpan<" + WasmCode.WasmValue + ">.Empty";
                break;
            case 1:
                writer.WriteLine("var __argument = " + WasmCode.ToWasmValue(parameters[0].Marshalling, parameters[0].Name) + ";");
                argumentsExpression = "new global::System.ReadOnlySpan<" + WasmCode.WasmValue + ">(in __argument)";
                break;
            default:
                writer.WriteLine("var __arguments = new __WasmValues" + parameters.Count.ToString(CultureInfo.InvariantCulture) + "();");
                for (var i = 0; i < parameters.Count; i++)
                {
                    writer.WriteLine("__arguments[" + i.ToString(CultureInfo.InvariantCulture) + "] = " + WasmCode.ToWasmValue(parameters[i].Marshalling, parameters[i].Name) + ";");
                }

                argumentsExpression = "__arguments";
                break;
        }

        switch (function.Results.Count)
        {
            case 0:
                writer.WriteLine(member.FieldName + ".Invoke(" + argumentsExpression + ", global::System.Span<" + WasmCode.WasmValue + ">.Empty);");
                break;
            case 1:
                writer.WriteLine(WasmCode.WasmValue + " __result = default;");
                writer.WriteLine(member.FieldName + ".Invoke(" + argumentsExpression + ", new global::System.Span<" + WasmCode.WasmValue + ">(ref __result));");
                writer.WriteLine("return " + FromResult(function.Results[0], "__result") + ";");
                break;
            default:
                writer.WriteLine("var __results = new __WasmValues" + function.Results.Count.ToString(CultureInfo.InvariantCulture) + "();");
                writer.WriteLine(member.FieldName + ".Invoke(" + argumentsExpression + ", __results);");
                var values = function.Results.Select((result, index) => FromResult(result, "__results[" + index.ToString(CultureInfo.InvariantCulture) + "]"));
                writer.WriteLine("return (" + string.Join(", ", values) + ");");
                break;
        }

        writer.CloseBlock();
    }

    private static string FromResult(WasmTypeInfo type, string expression) => WasmCode.FromWasmValue(WasmCode.GetMarshalling(type), type.Nullable, expression);

    private static List<MemberModel> CreateMembers(WasmModuleDescription description, string className)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { className };
        names.UnionWith(ReservedNames);
        var members = new List<MemberModel>();
        var functionIndex = 0;
        foreach (var export in description.Exports)
        {
            var name = CSharpNaming.MakeUnique(CSharpNaming.ToPascalCase(export.Name, "Export"), names);
            var fieldName = export.Kind == WasmExportKind.Function ? "__function" + (functionIndex++).ToString(CultureInfo.InvariantCulture) : "";
            members.Add(new MemberModel(export, name, fieldName));
        }

        return members;
    }

    private static string GetExternTypeName(WasmExportKind kind) => kind switch
    {
        WasmExportKind.Table => "WasmTable",
        WasmExportKind.Memory => "WasmMemory",
        WasmExportKind.Global => "WasmGlobal",
        WasmExportKind.Tag => "WasmTag",
        _ => "WasmFunction",
    };

    private static string GetKindName(WasmExportKind kind) => kind.ToString().ToLowerInvariant();

    private static string FormatBytes(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 6 + 2);
        builder.Append('[');
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append("0x").Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.Append(']').ToString();
    }

    private sealed class MemberModel(WasmExportDescription export, string name, string fieldName)
    {
        public WasmExportDescription Export { get; } = export;

        public string Name { get; } = name;

        public string FieldName { get; } = fieldName;
    }

    private sealed record ModuleOutput(string FilePath, string? FullClassName, string HintName, string? Source, ImmutableEquatableArray<DiagnosticInfo> Diagnostics)
    {
        public static ModuleOutput Failed(string path, DiagnosticInfo diagnostic) => new(path, FullClassName: null, HintName: "", Source: null, new[] { diagnostic }.ToImmutableEquatableArray());
    }
}
