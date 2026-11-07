using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId;

internal static class CompilationExtensions
{
    public static bool SupportGuidCreateVersion7(this Compilation compilation)
    {
        var guidSymbol = compilation.GetTypeByMetadataName("System.Guid");
        if (guidSymbol is null)
            return false;

        foreach (var member in guidSymbol.GetMembers("CreateVersion7"))
        {
            if (member is IMethodSymbol { IsStatic: true, Parameters: [] })
                return true;
        }

        return false;
    }
}
