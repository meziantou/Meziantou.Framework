using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

[Obsolete("Use PublicApi instead.")]
public static class PublicApiStubGenerator
{
    public static PublicApiModel ReadModel(string assemblyPath) => PublicApi.ReadModel(assemblyPath);

    public static PublicApiModel ReadModel(Assembly assembly) => PublicApi.ReadModel(assembly);

    public static PublicApiModel ReadModel(Module module) => PublicApi.ReadModel(module);

    public static PublicApiModel ReadModel(Type type) => PublicApi.ReadModel(type);

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(PublicApiModel model, PublicApiFileLayout layout = PublicApiFileLayout.SingleFile) => PublicApi.GenerateFiles(model, layout);

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(PublicApiModel model, PublicApiGeneratorOptions options) => PublicApi.GenerateFiles(model, options);

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(string assemblyPath, PublicApiGeneratorOptions? options = null) => PublicApi.GenerateFiles(assemblyPath, options);

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(Assembly assembly, PublicApiGeneratorOptions? options = null) => PublicApi.GenerateFiles(assembly, options);

    public static void GenerateToDirectory(string assemblyPath, string outputDirectory, PublicApiGeneratorOptions? options = null) => PublicApi.GenerateToDirectory(assemblyPath, outputDirectory, options);

    public static void GenerateToDirectory(Assembly assembly, string outputDirectory, PublicApiGeneratorOptions? options = null) => PublicApi.GenerateToDirectory(assembly, outputDirectory, options);
}
