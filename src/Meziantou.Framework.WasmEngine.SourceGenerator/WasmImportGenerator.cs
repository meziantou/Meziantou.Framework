using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Meziantou.Framework.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

/// <summary>
/// Generates the <c>DefineWasmImports</c> method of the types whose methods are marked with <c>[WasmImport]</c>: it
/// creates a host function per method, whose type and conversions are written at build time.
/// </summary>
[Generator]
public sealed class WasmImportGenerator : IIncrementalGenerator
{
    internal const string OutputsTrackingName = "WasmImportOutputs";

    private const string AttributeName = "Meziantou.Framework.WasmEngine.WasmImportAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The methods are grouped by their containing type: one method per type defines all its imports
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                (node, _) => node is MethodDeclarationSyntax,
                (context, _) => GetContainingType(context))
            .Where(type => type is not null)
            .Collect();

        var outputs = methods
            .Combine(context.CompilationProvider)
            .Select((pair, cancellationToken) => CreateOutputs(pair.Left, pair.Right, cancellationToken))
            .WithTrackingName(OutputsTrackingName);

        context.RegisterSourceOutput(outputs.Combine(context.CompilationProvider), (context, input) =>
        {
            var trees = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
            foreach (var tree in input.Right.SyntaxTrees)
            {
                trees[tree.FilePath] = trees.ContainsKey(tree.FilePath) ? null : tree;
            }

            foreach (var output in input.Left)
            {
                foreach (var diagnostic in output.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic.ToDiagnostic(trees));
                }

                if (output.Source is not null)
                {
                    context.AddSource(output.HintName, output.Source);
                }
            }
        });
    }

    private static string? GetContainingType(GeneratorAttributeSyntaxContext context)
        => context.TargetSymbol.ContainingType?.GetDocumentationCommentId();

    private static ImmutableEquatableArray<ImportOutput> CreateOutputs(IEnumerable<string?> typeIds, Compilation compilation, CancellationToken cancellationToken)
    {
        var attributeType = compilation.GetTypeByMetadataName(AttributeName);
        var outputs = new List<ImportOutput>();
        if (attributeType is null)
            return outputs.ToImmutableEquatableArray();

        var knownTypes = new KnownTypes(compilation);
        foreach (var typeId in typeIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(typeId!, compilation) is INamedTypeSymbol type)
            {
                outputs.Add(CreateOutput(type, attributeType, knownTypes));
            }
        }

        return outputs.ToImmutableEquatableArray();
    }

    private static ImportOutput CreateOutput(INamedTypeSymbol type, INamedTypeSymbol attributeType, KnownTypes knownTypes)
    {
        var hintName = GetHintName(type);
        var diagnostics = new List<DiagnosticInfo>();
        var typeLocation = type.Locations.FirstOrDefault(location => location.IsInSource);
        if (!ValidateType(type, typeLocation, diagnostics))
            return new ImportOutput(hintName, Source: null, diagnostics.ToImmutableEquatableArray());

        var imports = new List<ImportModel>();
        var definedBy = new Dictionary<(string Module, string Name), string>();
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().OrderBy(method => method.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0))
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                    continue;

                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault();
                if (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[0].Value is not string module || attribute.ConstructorArguments[1].Value is not string name)
                    continue;

                if (CreateImport(method, module, name, location, knownTypes, diagnostics) is not { } import)
                    continue;

                var methodName = method.ToDisplayString();
                if (definedBy.TryGetValue((module, name), out var existing))
                {
                    diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.DuplicateImport, location, module, name, methodName, existing));
                    continue;
                }

                definedBy.Add((module, name), methodName);
                imports.Add(import);
            }
        }

        if (diagnostics.Count > 0)
            return new ImportOutput(hintName, Source: null, diagnostics.ToImmutableEquatableArray());

        return new ImportOutput(hintName, Emit(type, imports), ImmutableEquatableArray<DiagnosticInfo>.Empty);
    }

    private static bool ValidateType(INamedTypeSymbol type, Location? location, List<DiagnosticInfo> diagnostics)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.UnsupportedType, location, type.ToDisplayString(), "not a class or a struct"));
            return false;
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.UnsupportedType, location, type.ToDisplayString(), ReferenceEquals(current, type) ? "generic" : "nested in a generic type"));
                return false;
            }

            if (current.IsFileLocal)
            {
                diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.UnsupportedType, location, type.ToDisplayString(), "file-local"));
                return false;
            }

            var isPartial = current.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(modifier => modifier.ValueText == "partial"));
            if (!isPartial)
            {
                diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.TypeMustBePartial, location, type.ToDisplayString()));
                return false;
            }
        }

        return true;
    }

    private static ImportModel? CreateImport(IMethodSymbol method, string module, string name, Location? location, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        string? reason = null;
        if (method.MethodKind != MethodKind.Ordinary)
        {
            reason = "it is not an ordinary method";
        }
        else if (method.IsGenericMethod)
        {
            reason = "it is generic";
        }
        else if (method.IsAbstract)
        {
            reason = "it is abstract";
        }
        else if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            reason = "it returns a reference";
        }
        else if (!method.IsStatic && method.ContainingType.TypeKind == TypeKind.Struct)
        {
            reason = "it is an instance method of a struct, which a host function cannot capture";
        }

        var parameters = new List<(ValueMarshalling Marshalling, WasmTypeInfo Type)>();
        foreach (var parameter in method.Parameters)
        {
            if (reason is not null)
                break;

            if (parameter.RefKind != RefKind.None)
            {
                reason = "parameter '" + parameter.Name + "' is passed by reference";
            }
            else if (knownTypes.GetValue(parameter.Type) is { } value)
            {
                parameters.Add(value);
            }
            else
            {
                reason = "the type '" + parameter.Type.ToDisplayString() + "' of parameter '" + parameter.Name + "' is not supported";
            }
        }

        var results = new List<(ValueMarshalling Marshalling, WasmTypeInfo Type)>();
        if (reason is null && !method.ReturnsVoid)
        {
            if (method.ReturnType is INamedTypeSymbol { IsTupleType: true } tuple)
            {
                foreach (var element in tuple.TupleElements)
                {
                    if (knownTypes.GetValue(element.Type) is { } value)
                    {
                        results.Add(value);
                    }
                    else
                    {
                        reason = "the type '" + element.Type.ToDisplayString() + "' of the tuple returned by the method is not supported";
                        break;
                    }
                }
            }
            else if (knownTypes.GetValue(method.ReturnType) is { } value)
            {
                results.Add(value);
            }
            else
            {
                reason = "the return type '" + method.ReturnType.ToDisplayString() + "' is not supported";
            }
        }

        if (reason is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(WasmDiagnostics.UnsupportedMethod, location, method.ToDisplayString(), reason));
            return null;
        }

        return new ImportModel(module, name, method.Name, method.IsStatic, parameters, results);
    }

    private static string Emit(INamedTypeSymbol type, List<ImportModel> imports)
    {
        const string Ns = WasmCode.Namespace;
        var writer = new CodeWriter();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("#nullable enable");
        writer.WriteLine("#pragma warning disable CS0612, CS0618 // The methods can be obsolete");
        writer.WriteLine();

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            writer.WriteLine("namespace " + type.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)));
            writer.OpenBlock();
        }

        var containingTypes = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            containingTypes.Insert(0, current);
        }

        foreach (var containingType in containingTypes)
        {
            writer.WriteLine("partial " + GetTypeKeyword(containingType) + " @" + containingType.Name);
            writer.OpenBlock();
        }

        var isStatic = imports.All(import => import.IsStatic);
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        writer.WriteLine("/// <summary>Defines the WebAssembly imports of the methods marked with <see cref=\"" + Ns + "WasmImportAttribute\"/>: a host function per method.</summary>");
        writer.WriteLine("/// <param name=\"store\">The store that creates the host functions.</param>");
        writer.WriteLine("/// <param name=\"imports\">The imports to define, or null to create them.</param>");
        writer.WriteLine("/// <returns>The imports.</returns>");
        writer.WriteLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Meziantou.Framework.WasmEngine.SourceGenerator\", \"1.0.0\")]");
        writer.WriteLine("public " + (isStatic ? "static " : "") + Ns + "WasmImports DefineWasmImports(" + Ns + "WasmStore store, " + Ns + "WasmImports? imports = null)");
        writer.OpenBlock();
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(store);");
        writer.WriteLine("imports ??= new " + Ns + "WasmImports();");
        foreach (var import in imports)
        {
            var parameterTypes = WasmCode.GetValueTypesExpression(import.Parameters.Select(parameter => parameter.Type));
            var resultTypes = WasmCode.GetValueTypesExpression(import.Results.Select(result => result.Type));
            writer.WriteLine("imports.Define(" + WasmCode.Literal(import.Module) + ", " + WasmCode.Literal(import.Name) + ", store.CreateFunction(new " + Ns + "WasmFunctionType(" + parameterTypes + ", " + resultTypes + "), (__arguments, __results) =>");
            writer.OpenBlock();
            var arguments = string.Join(", ", import.Parameters.Select((parameter, index) => WasmCode.FromWasmValue(parameter.Marshalling, parameter.Type.Nullable, "__arguments[" + index.ToString(CultureInfo.InvariantCulture) + "]")));
            var call = (import.IsStatic ? typeName : "this") + ".@" + import.MethodName + "(" + arguments + ")";
            switch (import.Results.Count)
            {
                case 0:
                    writer.WriteLine(call + ";");
                    break;
                case 1:
                    writer.WriteLine("__results[0] = " + WasmCode.ToWasmValue(import.Results[0].Marshalling, call) + ";");
                    break;
                default:
                    writer.WriteLine("var __value = " + call + ";");
                    for (var i = 0; i < import.Results.Count; i++)
                    {
                        var index = i.ToString(CultureInfo.InvariantCulture);
                        writer.WriteLine("__results[" + index + "] = " + WasmCode.ToWasmValue(import.Results[i].Marshalling, "__value.Item" + (i + 1).ToString(CultureInfo.InvariantCulture)) + ";");
                    }

                    break;
            }

            writer.CloseBlockWith("}));");
        }

        writer.WriteLine("return imports;");
        writer.CloseBlock();

        if (imports.Any(import => import.Parameters.Any(parameter => parameter.Marshalling == ValueMarshalling.HostObject)))
        {
            writer.WriteLine();
            WasmCode.WriteHostValueHelper(writer);
        }

        foreach (var _ in containingTypes)
        {
            writer.CloseBlock();
        }

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            writer.CloseBlock();
        }

        return writer.ToString();
    }

    private static string GetTypeKeyword(INamedTypeSymbol type) => type switch
    {
        { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        { TypeKind: TypeKind.Interface } => "interface",
        _ => "class",
    };

    // Replacing the characters of the name is not injective, and the compiler compares the hint names ignoring case: the
    // hash of the name keeps the names of distinct types distinct
    private static string GetHintName(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
        var builder = new StringBuilder(name.Length + 24);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_');
        }

        return builder.Append('.').Append(StableHash.Compute(name)).Append(".WasmImports.g.cs").ToString();
    }

    private sealed record ImportModel(string Module, string Name, string MethodName, bool IsStatic, List<(ValueMarshalling Marshalling, WasmTypeInfo Type)> Parameters, List<(ValueMarshalling Marshalling, WasmTypeInfo Type)> Results);

    private sealed record ImportOutput(string HintName, string? Source, ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

    private sealed class KnownTypes(Compilation compilation)
    {
        private readonly INamedTypeSymbol? _function = compilation.GetTypeByMetadataName("Meziantou.Framework.WasmEngine.WasmFunction");

        /// <summary>The conversion and the WebAssembly type of a .NET type: the reference types are nullable unless the type is annotated as not nullable.</summary>
        public (ValueMarshalling Marshalling, WasmTypeInfo Type)? GetValue(ITypeSymbol type)
        {
            var nullable = type.NullableAnnotation != NullableAnnotation.NotAnnotated;
            return type.SpecialType switch
            {
                SpecialType.System_Int32 => (ValueMarshalling.Int32, WasmTypeInfo.I32),
                SpecialType.System_UInt32 => (ValueMarshalling.UInt32, WasmTypeInfo.I32),
                SpecialType.System_Int64 => (ValueMarshalling.Int64, WasmTypeInfo.I64),
                SpecialType.System_UInt64 => (ValueMarshalling.UInt64, WasmTypeInfo.I64),
                SpecialType.System_Single => (ValueMarshalling.Single, WasmTypeInfo.F32),
                SpecialType.System_Double => (ValueMarshalling.Double, WasmTypeInfo.F64),
                SpecialType.System_Object => (ValueMarshalling.HostObject, WasmTypeInfo.Reference(WasmHeapTypeKind.Extern, nullable)),
                _ when _function is not null && SymbolEqualityComparer.Default.Equals(type, _function) => (ValueMarshalling.Function, WasmTypeInfo.Reference(WasmHeapTypeKind.Func, nullable)),
                _ => null,
            };
        }
    }
}
