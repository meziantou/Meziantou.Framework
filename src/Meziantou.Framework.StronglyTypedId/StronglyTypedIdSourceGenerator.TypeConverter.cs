namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateTypeConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateTypeConverter())
            return;

        using (writer.BeginBlock($"partial class {context.TypeConverterTypeName} : global::System.ComponentModel.TypeConverter"))
        {
            var idType = context.IdType;

            // public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            WriteNewMember(writer, context, addNewLine: false);
            using (writer.BeginBlock("public override bool CanConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type sourceType)"))
            {
                writer.WriteLine($"return sourceType == typeof(string) || sourceType == typeof({GetTypeReference(idType)}) || sourceType == typeof({context.TypeName});");
            }

            // public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override object? ConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object value)"))
            {
                using (writer.BeginBlock("if (value == null)"))
                {
                    writer.WriteLine($"return default({context.TypeName});");
                }

                using (writer.BeginBlock($"if (value is {GetTypeReference(idType)} typedValue)"))
                {
                    writer.WriteLine($"return {context.TypeName}.From{GetShortName(idType)}(typedValue);");
                }

                using (writer.BeginBlock($"if (value is string stringValue)"))
                {
                    writer.WriteLine($"return {context.TypeName}.Parse(stringValue);");
                }

                writer.WriteLine($"throw new global::System.ArgumentException($\"Cannot convert '{{value}}' to {context.TypeName}\", nameof(value));");
            }

            // public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override bool CanConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type? destinationType)"))
            {
                writer.WriteLine($"return destinationType != null && (destinationType == typeof(string) || destinationType == typeof({GetTypeReference(idType)}) || destinationType == typeof({context.TypeName}));");
            }

            // public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override object? ConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object? value, global::System.Type destinationType)"))
            {
                using (writer.BeginBlock("if (value != null)"))
                {

                    using (writer.BeginBlock("if (destinationType == typeof(string))"))
                    {
                        writer.WriteLine($"return (({context.TypeName})value).ValueAsString;");
                    }

                    using (writer.BeginBlock($"if (destinationType == typeof({GetTypeReference(idType)}))"))
                    {
                        writer.WriteLine($"return (({context.TypeName})value).Value;");
                    }

                    using (writer.BeginBlock($"if (destinationType == typeof({context.TypeName}))"))
                    {
                        writer.WriteLine($"return value;");
                    }

                }

                writer.WriteLine("throw new global::System.InvalidOperationException($\"Cannot convert '{value}' to '{destinationType}'\");");
            }
        }
    }
}
