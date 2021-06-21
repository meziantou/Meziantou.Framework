using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.FastEnumToStringGenerator
{
    [Generator]
    public sealed partial class EnumToStringSourceGenerator : ISourceGenerator
    {
        [SuppressMessage("Usage", "MA0101:String contains an implicit end of line character", Justification = "Not important")]
        private const string AttributeText = @"
#nullable enable
[System.Diagnostics.Conditional(""FastEnumToString_Attributes"")]
[System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class FastEnumToStringAttribute : System.Attribute
{
    public FastEnumToStringAttribute(System.Type enumType)
    {
    }

    public bool IsPublic { get; set; } = true;
    public string? ExtensionMethodNamespace { get; set; }
}
";

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("FastEnumToStringAttribute.g.cs", SourceText.From(AttributeText, Encoding.UTF8));

            var options = (CSharpParseOptions)((CSharpCompilation)context.Compilation).SyntaxTrees[0].Options;
            var compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options, cancellationToken: context.CancellationToken));

            var attributeSymbol = compilation.GetTypeByMetadataName("FastEnumToStringAttribute");
            var flagsAttributeSymbol = compilation.GetTypeByMetadataName("System.FlagsAttribute");
            if (attributeSymbol == null || flagsAttributeSymbol == null)
                return;

            var enums = new List<EnumToProcess>();
            foreach (var attr in compilation.Assembly.GetAttributes())
            {
                if (!attributeSymbol.Equals(attr.AttributeClass, SymbolEqualityComparer.Default))
                    continue;

                if (attr.ConstructorArguments.Length != 1)
                    continue;

                var arg = attr.ConstructorArguments[0];
                if (arg.Value == null)
                    continue;

                var enumType = (ITypeSymbol)arg.Value;
                if (enumType.TypeKind != TypeKind.Enum)
                    continue;

                enums.Add(new EnumToProcess(enumType, GetMembers(), IsPublic(), GetNamespace()));

                List<EnumMemberToProcess> GetMembers()
                {
                    var result = new List<EnumMemberToProcess>();
                    foreach (var member in enumType.GetMembers())
                    {
                        if (member is not IFieldSymbol field)
                            continue;

                        if (field.ConstantValue is null)
                            continue;

                        result.Add(new(member.Name, field.ConstantValue));
                    }

                    return result;
                }

                bool IsPublic()
                {
                    var result = IsVisibleOutsideOfAssembly(enumType);
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "IsPublic")
                        {
                            return result && (bool)arg.Value.Value!;
                        }
                    }

                    return result;
                }

                string? GetNamespace()
                {
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "ExtensionMethodNamespace")
                            return (string?)arg.Value.Value;
                    }

                    return null;
                }
            }

            var code = GenerateCode(enums);
            context.AddSource("FastEnumToStringExtensions.g.cs", SourceText.From(code, Encoding.UTF8));


            //static bool IsFlags(ITypeSymbol typeSymbol, ISymbol flagsAttributeSymbol)
            //{
            //    foreach (var arg in typeSymbol.GetAttributes())
            //    {
            //        if (flagsAttributeSymbol.Equals(arg.AttributeClass, SymbolEqualityComparer.Default))
            //            return true;
            //    }

            //    return false;
            //}
        }

        private static string GenerateCode(List<EnumToProcess> enums)
        {
            var sb = new StringBuilder();

            foreach (var enumerationGroup in enums.GroupBy(en => en.FullNamespace, StringComparer.Ordinal))
            {
                var typeVisibility = enumerationGroup.Any(enumeration => enumeration.IsPublic) ? "public" : "internal";
                foreach (var enumeration in enumerationGroup)
                {
                    var methodVisibility = enumeration.IsPublic ? "public" : "internal";

                    if (!string.IsNullOrEmpty(enumeration.FullNamespace))
                    {
                        sb.Append("namespace ").Append(enumeration.FullNamespace).AppendLine();
                        sb.AppendLine("{");
                    }

                    sb.Append(typeVisibility).AppendLine(" static partial class FastEnumToStringExtensions");
                    sb.AppendLine("{");

                    sb.Append("    ").Append(methodVisibility).Append(" static string ToStringFast(this ").Append(enumeration.FullCsharpName).AppendLine(" value)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        return value switch");
                    sb.AppendLine("        {");
                    foreach (var member in enumeration.Members)
                    {
                        sb.Append("            ").Append(enumeration.FullCsharpName).Append('.').Append(member.Name).Append(" => nameof(").Append(enumeration.FullCsharpName).Append('.').Append(member.Name).AppendLine("),");
                    }

                    sb.AppendLine("        _ => value.ToString(),");
                    sb.AppendLine("        };");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");

                    if (!string.IsNullOrEmpty(enumeration.FullNamespace))
                    {
                        sb.AppendLine("}");
                    }
                }
            }

            return sb.ToString();
        }
        private static bool IsVisibleOutsideOfAssembly([NotNullWhen(true)] ISymbol? symbol)
        {
            if (symbol == null)
                return false;

            if (symbol.DeclaredAccessibility != Accessibility.Public &&
                symbol.DeclaredAccessibility != Accessibility.Protected &&
                symbol.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            {
                return false;
            }

            if (symbol.ContainingType == null)
                return true;

            return IsVisibleOutsideOfAssembly(symbol.ContainingType);
        }

        private record EnumToProcess(ITypeSymbol EnumSymbol, List<EnumMemberToProcess> Members, bool IsPublic, string? Namespace)
        {
            public string FullCsharpName => EnumSymbol.ToString()!;
            public string? FullNamespace => Namespace ?? GetNamespace(EnumSymbol);

            private static string? GetNamespace(ITypeSymbol symbol)
            {
                string? result = null;
                var ns = symbol.ContainingNamespace;
                while (ns != null && !ns.IsGlobalNamespace)
                {
                    if (result != null)
                    {
                        result = ns.Name + "." + result;
                    }
                    else
                    {
                        result = ns.Name;
                    }

                    ns = ns.ContainingNamespace;
                }

                return result;
            }
        }

        private record EnumMemberToProcess(string Name, object Value);
    }
}
