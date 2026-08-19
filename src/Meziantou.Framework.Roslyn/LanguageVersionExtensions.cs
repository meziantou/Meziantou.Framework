#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn;

internal static partial class LanguageVersionExtensions
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

    public static bool IsCSharp8OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)800;
    }

    public static bool IsCSharp9OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)900;
    }

    public static bool IsCSharp10OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1000;
    }

    public static bool IsCSharp11OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1100;
    }

    public static bool IsCSharp12OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1200;
    }

    public static bool IsCSharp13OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1300;
    }

    public static bool IsCSharp14OrGreater(this LanguageVersion languageVersion)
    {
        return languageVersion >= (LanguageVersion)1400;
    }

    public static bool IsCSharp15OrGreater(this LanguageVersion languageVersion)
    {
#if ROSLYN_5_6_OR_GREATER
        return languageVersion >= (LanguageVersion)1500 || languageVersion is LanguageVersion.Preview;
#else
        return false;
#endif
    }
}
