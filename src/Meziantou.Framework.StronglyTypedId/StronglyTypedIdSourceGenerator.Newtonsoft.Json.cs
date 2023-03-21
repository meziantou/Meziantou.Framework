namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateNewtonsoftJsonConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateNewtonsoftJsonConverter())
            return;

        var idType = context.IdType;

        using (writer.BeginBlock($"partial class {context.NewtonsoftJsonConverterTypeName} : global::Newtonsoft.Json.JsonConverter"))
        {
            WriteNewMember(writer, context, addNewLine: false);
            writer.WriteLine("public override bool CanRead => true;");

            WriteNewMember(writer, context, addNewLine: true);
            writer.WriteLine("public override bool CanWrite => true;");

            WriteNewMember(writer, context, addNewLine: true);
            writer.WriteLine($"public override bool CanConvert(global::System.Type type) => type == typeof({context.TypeName});");

            // public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override void WriteJson(global::Newtonsoft.Json.JsonWriter writer, object? value, global::Newtonsoft.Json.JsonSerializer serializer)"))
            {
                using (writer.BeginBlock("if (value == null)"))
                {
                    writer.WriteLine("writer.WriteNull();");
                }
                using (writer.BeginBlock("else"))
                {
                    if (idType is IdType.System_Half)
                    {
                        writer.WriteLine($"writer.WriteValue((float)(({context.TypeName})value).Value);");
                    }
                    else if (idType is IdType.System_Int128 or IdType.System_UInt128)
                    {
                        writer.WriteLine($"writer.WriteRawValue((({context.TypeName})value).ValueAsString);");
                    }
                    else
                    {
                        writer.WriteLine($"writer.WriteValue((({context.TypeName})value).Value);");
                    }
                }
            }

            // public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock("public override object? ReadJson(global::Newtonsoft.Json.JsonReader reader, global::System.Type objectType, object? existingValue, global::Newtonsoft.Json.JsonSerializer serializer)"))
            {
                using (writer.BeginBlock("if (reader.TokenType == global::Newtonsoft.Json.JsonToken.StartObject)"))
                {
                    writer.WriteLine("object? value = null;");
                    writer.WriteLine("bool valueRead = false;");
                    writer.WriteLine("reader.Read();");
                    using (writer.BeginBlock("while (reader.TokenType != global::Newtonsoft.Json.JsonToken.EndObject)"))
                    {
                        using (writer.BeginBlock("if (!valueRead && reader.TokenType == global::Newtonsoft.Json.JsonToken.PropertyName && ((string?)reader.Value) == \"Value\")"))
                        {
                            writer.WriteLine("reader.Read();");
                            ReadValue("value = ");
                            writer.WriteLine("valueRead = true;");
                            writer.WriteLine("reader.Read();");
                        }
                        using (writer.BeginBlock("else"))
                        {
                            writer.WriteLine("reader.Skip();");
                            writer.WriteLine("reader.Read();");
                        }
                    }

                    writer.WriteLine("return value;");
                }
                ReadValue("return ");
            }

            void ReadValue(string? left = null)
            {
                using (writer.BeginBlock($"if (reader.TokenType == global::Newtonsoft.Json.JsonToken.Null{(context.IsReferenceType ? "" : (" && objectType == typeof(global::System.Nullable<" + context.TypeName + ">)"))})"))
                {
                    writer.WriteLine($"{left}null;");
                }
                using (writer.BeginBlock("else"))
                {
                    if (idType is IdType.System_Half)
                    {
                        writer.WriteLine($"{left}new {context.TypeName}(({context.ValueTypeCSharpTypeName})serializer.Deserialize<float>(reader));");
                    }
                    else if (idType is IdType.System_Int128 or IdType.System_UInt128)
                    {
                        writer.WriteLine($"{left}new {context.TypeName}(({context.ValueTypeCSharpTypeName})serializer.Deserialize<global::System.Numerics.BigInteger>(reader));");
                    }
                    else if (idType is IdType.MongoDB_Bson_ObjectId)
                    {
                        writer.WriteLine($"{left}{context.TypeName}.Parse(serializer.Deserialize<string>(reader));");
                    }
                    else
                    {
                        if (idType is IdType.System_String)
                        {
                            writer.WriteLine($"#nullable disable");
                        }

                        writer.WriteLine($"{left}new {context.TypeName}(serializer.Deserialize<{context.ValueTypeCSharpTypeName}>(reader));");

                        if (idType is IdType.System_String)
                        {
                            writer.WriteLine($"#nullable enable");
                        }
                    }
                }
            }
        }
    }
}
