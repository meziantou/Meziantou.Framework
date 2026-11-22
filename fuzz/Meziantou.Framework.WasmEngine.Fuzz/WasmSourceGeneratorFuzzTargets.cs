using System.Reflection;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.SourceGeneration;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.WasmEngine.Fuzz;

/// <summary>
/// The targets of the readers of the source generator (Meziantou.Framework.WasmEngine.SourceGenerator), which read the
/// exports of a module of the binary or of the text format to generate its class:
/// <list type="bullet">
/// <item>A reader only throws a <see cref="WasmDescriptionException"/>, at a position of the input.</item>
/// <item>A module that the engine compiles is read, and the reader returns the exports and the types of the engine.</item>
/// <item>The names of the members and of the parameters of the generated class are C# identifiers.</item>
/// </list>
/// </summary>
internal static class WasmSourceGeneratorFuzzTargets
{
    private const string ResourcePrefix = "TestModules.";

    private static readonly WasmRuntime Runtime = new(new WasmRuntimeOptions { MaxMemorySize = 16 * 1024 * 1024 });

    public static void BinaryTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        WasmModuleDescription? description = null;
        try
        {
            description = WasmBinaryDescriptionReader.Read(bytes);
        }
        catch (WasmDescriptionException ex)
        {
            FuzzAssert.True(ex.Position >= 0 && ex.Position <= bytes.Length, $"The position {ex.Position} of the error is not in the module");
        }

        if (description is not null)
        {
            CheckNames(description);
        }

        WasmModule module;
        try
        {
            module = Runtime.Compile(bytes);
        }
        catch (WasmCompileException)
        {
            return;
        }

        FuzzAssert.True(description is not null, "The engine compiles the module, but the generator cannot read it");
        CheckExports(description, module);
    }

    public static void TextTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        WasmModuleDescription? description = null;
        try
        {
            description = WatDescriptionReader.Read(text);
        }
        catch (WasmDescriptionException ex)
        {
            // The error of a binary module (module binary "...") is a position in its bytes
            FuzzAssert.True(ex.Position >= 0, $"The position {ex.Position} of the error is negative");
        }

        if (description is not null)
        {
            CheckNames(description);
        }

        WasmModule module;
        try
        {
            module = Runtime.Compile(WasmRuntime.WatToWasm(text));
        }
        catch (Exception ex) when (ex is FormatException or WasmCompileException)
        {
            return;
        }

        FuzzAssert.True(description is not null, "The engine compiles the module, but the generator cannot read it");
        CheckExports(description, module);
    }

    public static IEnumerable<FuzzSeed> GetBinarySeeds()
    {
        foreach (var (name, content) in GetTestModules())
        {
            yield return new FuzzSeed(name, name.EndsWith(".wat", StringComparison.Ordinal) ? WasmRuntime.WatToWasm(System.Text.Encoding.UTF8.GetString(content)) : content);
        }
    }

    public static IEnumerable<FuzzSeed> GetTextSeeds()
    {
        foreach (var (name, content) in GetTestModules())
        {
            if (name.EndsWith(".wat", StringComparison.Ordinal))
            {
                yield return FuzzSeed.FromText(name, System.Text.Encoding.UTF8.GetString(content));
            }
        }
    }

    // The modules of the tests of the generated classes (tests/Meziantou.Framework.WasmEngine.Tests/TestModules)
    private static IEnumerable<(string Name, byte[] Content)> GetTestModules()
    {
        var assembly = typeof(WasmSourceGeneratorFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return ("generator-" + resourceName[ResourcePrefix.Length..], memory.ToArray());
        }
    }

    private static void CheckExports(WasmModuleDescription description, WasmModule module)
    {
        FuzzAssert.Equal(module.Exports.Count, description.Exports.Count, "The number of exports");
        var exports = description.Exports.ToDictionary(export => export.Name, StringComparer.Ordinal);
        foreach (var expected in module.Exports)
        {
            FuzzAssert.True(exports.TryGetValue(expected.Name, out var actual), $"The generator does not read the export {FuzzAssert.Format(expected.Name)}");
            FuzzAssert.Equal(expected.Type.Kind, (WasmExternKind)actual.Kind, $"The kind of the export {FuzzAssert.Format(expected.Name)}");
            if (expected.Type is WasmFunctionType functionType)
            {
                var signature = actual.Function;
                FuzzAssert.True(signature is not null, $"The export {FuzzAssert.Format(expected.Name)} has no type");
                FuzzAssert.Equal(signature.Parameters.Count, signature.ParameterNames.Count, "The number of parameter names");
                CheckTypes(functionType.Parameters, signature.Parameters, expected.Name);
                CheckTypes(functionType.Results, signature.Results, expected.Name);
            }
        }
    }

    private static void CheckTypes(WasmValueType[] expected, IReadOnlyList<WasmTypeInfo> actual, string name)
    {
        FuzzAssert.Equal(expected.Length, actual.Count, $"The number of types of the export {FuzzAssert.Format(name)}");
        for (var i = 0; i < expected.Length; i++)
        {
            var type = new WasmValueType((WasmValueKind)actual[i].Kind, (WasmHeapKind)actual[i].Heap, actual[i].Nullable);
            FuzzAssert.Equal(expected[i], type, $"The type {i} of the export {FuzzAssert.Format(name)}");
        }
    }

    private static void CheckNames(WasmModuleDescription description)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in description.Exports)
        {
            var name = CSharpNaming.MakeUnique(CSharpNaming.ToPascalCase(export.Name, "Export"), names);
            FuzzAssert.True(CSharpNaming.IsValidIdentifier(name), $"The member name {FuzzAssert.Format(name)} of the export {FuzzAssert.Format(export.Name)} is not an identifier");
            if (export.Function is { } function)
            {
                foreach (var parameterName in function.ParameterNames)
                {
                    if (parameterName is null)
                        continue;

                    var parameter = CSharpNaming.ToCamelCase(parameterName, "arg0");
                    FuzzAssert.True(SyntaxFacts.IsValidIdentifier(parameter.TrimStart('@')) && (parameter[0] == '@' || SyntaxFacts.GetKeywordKind(parameter) == SyntaxKind.None), $"The parameter name {FuzzAssert.Format(parameter)} of {FuzzAssert.Format(parameterName)} is not an identifier");
                }
            }
        }
    }
}
