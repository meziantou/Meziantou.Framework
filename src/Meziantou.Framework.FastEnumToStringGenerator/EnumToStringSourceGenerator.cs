using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
[System.Diagnostics.Conditional(""FastEnumToString_Attributes"")]
[System.AttributeUsage(System.AttributeTargets.Assembly)]
internal sealed class FastEnumToStringAttribute : System.Attribute
{
    public FastEnumToStringAttribute(System.Type enumType)
    {
    }
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

                enums.Add(new EnumToProcess(enumType, GetMembers()));

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
            sb.AppendLine("internal static partial class FastEnumToStringExtensions");
            sb.AppendLine("{");

            foreach (var enumeration in enums)
            {
                var typeName = enumeration.EnumSymbol.ToString();
                sb.Append("    internal static string ToStringFast(this ").Append(typeName).AppendLine(" value)");
                sb.AppendLine("    {");
                sb.AppendLine("        return value switch");
                sb.AppendLine("        {");
                foreach (var member in enumeration.Members)
                {
                    sb.Append("            ").Append(typeName).Append('.').Append(member.Name).Append(" => nameof(").Append(typeName).Append('.').Append(member.Name).AppendLine("),");
                }

                sb.AppendLine("        _ => value.ToString(),");
                sb.AppendLine("        };");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private record EnumToProcess(ITypeSymbol EnumSymbol, List<EnumMemberToProcess> Members);

        private record EnumMemberToProcess(string Name, object Value);
    }
}
