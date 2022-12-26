namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateTypeConverter(CSharpGeneratedFileWriter writer, StronglyTypedIdInfo context)
    {
        if (!context.CanGenerateTypeConverter())
            return;

        using (writer.BeginBlock($"partial class {context.TypeConverterTypeName} : global::System.ComponentModel.TypeConverter"))
        {
            var idType = context.AttributeInfo.IdType;

            // public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            WriteNewMember(writer, context, addNewLine: false);
            using (writer.BeginBlock("public override bool CanConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type sourceType)"))
            {
                writer.WriteLine($"return sourceType == typeof(string) || sourceType == typeof({GetTypeReference(idType)}) || sourceType == typeof({context.Name});");
            }

            // public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override object? ConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object value)"))
            {
                using (writer.BeginBlock("if (value == null)"))
                {
                    writer.WriteLine($"return default({context.Name});");
                }

                using (writer.BeginBlock($"if (value is {GetTypeReference(idType)} typedValue)"))
                {
                    writer.WriteLine($"return {context.Name}.From{GetShortName(idType)}(typedValue);");
                }

                using (writer.BeginBlock($"if (value is string stringValue)"))
                {
                    writer.WriteLine($"return {context.Name}.Parse(stringValue);");
                }

                writer.WriteLine($"throw new global::System.ArgumentException($\"Cannot convert '{{value}}' to {context.Name}\", nameof(value));");
            }

            // public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override bool CanConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type? destinationType)"))
            {
                writer.WriteLine($"return destinationType != null && (destinationType == typeof(string) || destinationType == typeof({GetTypeReference(idType)}) || destinationType == typeof({context.Name}));");
            }

            // public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override object? ConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object? value, global::System.Type destinationType)"))
            {
                using (writer.BeginBlock("if (value != null)"))
                {

                    using (writer.BeginBlock("if (destinationType == typeof(string))"))
                    {
                        writer.WriteLine($"return (({context.Name})value).ValueAsString;");
                    }

                    using (writer.BeginBlock($"if (destinationType == typeof({GetTypeReference(idType)}))"))
                    {
                        writer.WriteLine($"return (({context.Name})value).Value;");
                    }

                    using (writer.BeginBlock($"if (destinationType == typeof({context.Name}))"))
                    {
                        writer.WriteLine($"return value;");
                    }

                }

                writer.WriteLine("throw new global::System.InvalidOperationException($\"Cannot convert '{value}' to '{destinationType}'\");");
            }
        }
    }
}
