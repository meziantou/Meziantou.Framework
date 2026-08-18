#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn;

internal static class LanguageVersionExtensions
{
    public static LanguageVersion GetCSharpLanguageVersion(this IOperation operation)
    {
        if (operation.Syntax.SyntaxTree.Options is CSharpParseOptions options)
            return options.LanguageVersion;

        return LanguageVersion.Default;
    }

    public static LanguageVersion GetCSharpLanguageVersion(this SyntaxNode syntaxNode)
    {
        if (syntaxNode.SyntaxTree.Options is CSharpParseOptions options)
            return options.LanguageVersion;

        return LanguageVersion.Default;
    }

    public static LanguageVersion GetCSharpLanguageVersion(this SyntaxTree syntaxTree)
    {
        if (syntaxTree.Options is CSharpParseOptions options)
            return options.LanguageVersion;

        return LanguageVersion.Default;
    }

    public static LanguageVersion GetCSharpLanguageVersion(this Compilation compilation)
    {
        var syntaxTree = compilation.SyntaxTrees.FirstOrDefault();
        if (syntaxTree?.Options is CSharpParseOptions options)
            return options.LanguageVersion;

        return LanguageVersion.Default;
    }

    public static bool IsCSharp8OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)800;
    }

    public static bool IsCSharp9OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)900;
    }

    public static bool IsCSharp10OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1000;
    }

    public static bool IsCSharp11OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1100;
    }

    public static bool IsCSharp12OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1200;
    }

    public static bool IsCSharp13OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1300;
    }

    public static bool IsCSharp14OrAbove(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1400;
    }

    public static bool IsCSharp15OrAbove(this LanguageVersion languageVersion)
    {
#if ROSLYN_5_6_OR_GREATER
        return languageVersion >= (LanguageVersion)1500 || languageVersion is LanguageVersion.Preview;
#else
        return false;
#endif
    }
}
