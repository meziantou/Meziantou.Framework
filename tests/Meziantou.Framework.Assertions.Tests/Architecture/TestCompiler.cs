using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Meziantou.Framework.Assertions.Architecture.Tests;

// Compiles C# code into assemblies with an embedded PDB, whose source file is "/src/<AssemblyName>.cs"
internal static class TestCompiler
{
    private static readonly Lazy<MetadataReference[]> FrameworkReferences = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Select(path => MetadataReference.CreateFromFile(path))]);

    public static string GetSourcePath(string assemblyName) => "/src/" + assemblyName + ".cs";

    public static byte[] Compile(string assemblyName, string source, params IEnumerable<byte[]> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path: GetSourcePath(assemblyName), encoding: Encoding.UTF8);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            [.. FrameworkReferences.Value, .. references.Select(reference => MetadataReference.CreateFromImage(reference))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
        if (!result.Success)
            throw new InvalidOperationException("The code does not compile:\n" + string.Join('\n', result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return stream.ToArray();
    }

    public static ArchitectureModel Load(string source) => ArchitectureModel.FromBytes(Compile("Sample", source));
}
