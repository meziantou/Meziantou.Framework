namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private const string YamlNamespace = "global::Meziantou.Framework.Yaml.Serialization";

    private static void GenerateYamlConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateYamlConverter())
            return;

        // The value is read and written by the converter of the underlying type, so an ID uses the same YAML
        // representation as its value, including the custom converters registered in the options.
        // MongoDB.Bson.ObjectId has no YAML converter: it is written as a string.
        var isObjectId = context.IdType is IdType.MongoDB_Bson_ObjectId;
        var valueTypeName = isObjectId ? "string" : context.ValueTypeCSharpTypeName;

        WriteNewMember(writer, context, addNewLine: true, [XmlSummary("Converts ", XmlSeeCref(context.TypeName), " to and from YAML with Meziantou.Framework.Yaml.")]);
        using (writer.BeginBlock($"{context.GetConverterModifiers(StronglyTypedIdConverters.Meziantou_Framework_Yaml)}partial class {context.YamlConverterTypeName} : {YamlNamespace}.YamlConverter<{context.TypeName}>"))
        {
            WriteNewMember(writer, context, addNewLine: false, InheritDocComment);
            using (writer.BeginBlock($"public override {context.CSharpNullableTypeName} Read({YamlNamespace}.YamlReader reader)"))
            {
                if (context.IsReferenceType)
                {
                    using (writer.BeginBlock($"if (reader.TokenType == {YamlNamespace}.YamlTokenType.Scalar && {YamlNamespace}.YamlScalar.IsNull(reader))"))
                    {
                        writer.WriteLine("reader.Read();");
                        writer.WriteLine("return null;");
                    }
                }

                // The converter of the value can be a non-generic YamlConverter, for example one registered in the options
                writer.WriteLine($"var converter = reader.GetConverter(typeof({valueTypeName}));");
                using (writer.BeginBlock($"if (converter is {YamlNamespace}.YamlConverter<{valueTypeName}> typedConverter)"))
                {
                    WriteCreateId("typedConverter.Read(reader)");
                }

                WriteCreateId($"({valueTypeName})converter.Read(reader, typeof({valueTypeName}))");
            }

            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override void Write({YamlNamespace}.YamlWriter writer, {context.TypeName} value)"))
            {
                if (context.IsReferenceType)
                {
                    using (writer.BeginBlock("if (value is null)"))
                    {
                        writer.WriteLine("writer.WriteNullValue();");
                        writer.WriteLine("return;");
                    }
                }

                var value = isObjectId ? "value.Value.ToString()" : "value.Value";
                writer.WriteLine($"var converter = writer.GetConverter(typeof({valueTypeName}));");
                using (writer.BeginBlock($"if (converter is {YamlNamespace}.YamlConverter<{valueTypeName}> typedConverter)"))
                {
                    writer.WriteLine($"typedConverter.Write(writer, {value});");
                    writer.WriteLine("return;");
                }

                writer.WriteLine($"converter.Write(writer, {value});");
            }
        }

        void WriteCreateId(string valueExpression)
        {
            if (isObjectId)
            {
                writer.WriteLine($"return {context.TypeName}.Parse({valueExpression}!);");
                return;
            }

            if (context.IdType is not IdType.System_String)
            {
                writer.WriteLine($"return new {context.TypeName}({valueExpression}!);");
                return;
            }

            // A null string is a valid value of a string ID
            writer.WriteLine("#nullable disable");
            writer.WriteLine($"return new {context.TypeName}({valueExpression});");
            writer.WriteLine("#nullable enable");
        }
    }
}
