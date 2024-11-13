namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateSystemTextJsonConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateSystemTextJsonConverter())
            return;

        var idType = context.IdType;

        using (writer.BeginBlock($"partial class {context.SystemTextJsonConverterTypeName} : global::System.Text.Json.Serialization.JsonConverter<{context.TypeName}>"))
        {
            // public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] Sample value, JsonSerializerOptions options)
            WriteNewMember(writer, context, addNewLine: false, InheritDocComment);
            using (writer.BeginBlock($"public override void WriteAsPropertyName(global::System.Text.Json.Utf8JsonWriter writer, {(context.SupportDisallowNullAttribute ? "[global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute]" : "")}{context.TypeName} value, global::System.Text.Json.JsonSerializerOptions options)"))
            {
                writer.WriteLine("writer.WritePropertyName(value.ValueAsString);");
            }

            // public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override {context.TypeName} ReadAsPropertyName(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"))
            {
                writer.WriteLine($"return {context.TypeName}.Parse(reader.GetString()!);");
            }

            // public abstract void Write (System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options);
            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override void Write(global::System.Text.Json.Utf8JsonWriter writer, {context.TypeName} value, global::System.Text.Json.JsonSerializerOptions options)"))
            {
                if (context.IsReferenceType)
                {
                    using (writer.BeginBlock("if (value == null)"))
                    {
                        writer.WriteLine("writer.WriteNullValue();");
                        writer.WriteLine("return;");
                    }
                }

                if (idType == IdType.System_Boolean)
                {
                    writer.WriteLine("writer.WriteBooleanValue(value.Value);");
                }
                else if (idType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger or IdType.System_Half)
                {
                    writer.WriteLine("writer.WriteRawValue(value.ValueAsString);");
                }
                else if (idType is IdType.MongoDB_Bson_ObjectId)
                {
                    writer.WriteLine("writer.WriteStringValue(value.Value.ToString());");
                }
                else if (CanUseWriteNumberValue())
                {
                    writer.WriteLine("writer.WriteNumberValue(value.Value);");
                }
                else if (CanUseWriteNumberValueWithCastToInt())
                {
                    writer.WriteLine("writer.WriteNumberValue((int)value.Value);");
                }
                else if (CanUseWriteNumberValueWithCastToUInt())
                {
                    writer.WriteLine("writer.WriteNumberValue((uint)value.Value);");
                }
                else if (CanUseWriteStringValue())
                {
                    writer.WriteLine("writer.WriteStringValue(value.Value);");
                }
                else
                {
                    writer.WriteLine("global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value, options);");
                }

                bool CanUseWriteNumberValue()
                {
                    return idType == IdType.System_Decimal
                        || idType == IdType.System_Double
                        || idType == IdType.System_Int32
                        || idType == IdType.System_Int64
                        || idType == IdType.System_Single
                        || idType == IdType.System_UInt32
                        || idType == IdType.System_UInt64;
                }

                bool CanUseWriteNumberValueWithCastToInt()
                {
                    return idType == IdType.System_Int16
                        || idType == IdType.System_SByte;
                }

                bool CanUseWriteNumberValueWithCastToUInt()
                {
                    return idType == IdType.System_Byte
                        || idType == IdType.System_UInt16;
                }

                bool CanUseWriteStringValue()
                {
                    return idType == IdType.System_DateTime
                        || idType == IdType.System_DateTimeOffset
                        || idType == IdType.System_Guid
                        || idType == IdType.System_String;
                }
            }

            // public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override {context.TypeName} Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"))
            {
                using (writer.BeginBlock("if (reader.TokenType == global::System.Text.Json.JsonTokenType.StartObject)"))
                {
                    if (context.IsReferenceType)
                    {
                        writer.WriteLine($"{context.TypeName}? value = default;");
                    }
                    else
                    {
                        writer.WriteLine($"{context.TypeName} value = default;");
                    }

                    writer.WriteLine("bool valueRead = false;");
                    writer.WriteLine("reader.Read();");
                    using (writer.BeginBlock("while (reader.TokenType != global::System.Text.Json.JsonTokenType.EndObject)"))
                    {
                        using (writer.BeginBlock("if (!valueRead && reader.TokenType == global::System.Text.Json.JsonTokenType.PropertyName && reader.ValueTextEquals(\"Value\"))"))
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

                    if (context.IsReferenceType)
                    {
                        writer.WriteLine($"#nullable disable");
                        writer.WriteLine($"return value ?? new {context.TypeName}(default({context.ValueTypeCSharpTypeName}));");
                        writer.WriteLine($"#nullable enable");
                    }
                    else
                    {
                        writer.WriteLine("return value;");
                    }
                }

                ReadValue("return ");
            }
        }

        void ReadValue(string? left = null)
        {
            if (idType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger or IdType.System_Half or IdType.MongoDB_Bson_ObjectId)
            {
                writer.BeginBlock("if (reader.HasValueSequence)");
                writer.WriteLine($"{left}{context.TypeName}.Parse(global::System.Text.EncodingExtensions.GetString(global::System.Text.Encoding.UTF8, reader.ValueSequence));");
                writer.EndBlock();
                writer.BeginBlock("else");
                writer.WriteLine($"{left}{context.TypeName}.Parse(global::System.Text.Encoding.UTF8.GetString(reader.ValueSpan));");
                writer.EndBlock();
            }
            else
            {
                if (idType is IdType.System_String)
                {
                    writer.WriteLine($"#nullable disable");
                }

                writer.WriteLine($"{left}new {context.TypeName}(reader.Get{context.ValueTypeShortName}());");

                if (idType is IdType.System_String)
                {
                    writer.WriteLine($"#nullable enable");
                }
            }
        }
    }
}
