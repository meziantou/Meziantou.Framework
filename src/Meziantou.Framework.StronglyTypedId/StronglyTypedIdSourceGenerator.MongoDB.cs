namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateMongoDBBsonSerializationConverter(CSharpGeneratedFileWriter writer, AttributeInfo context)
    {
        if (!context.CanGenerateMongoDbConverter())
            return;

        using (writer.BeginBlock($"partial class {context.MongoDbConverterTypeName} : global::MongoDB.Bson.Serialization.Serializers.SerializerBase<{context.TypeName}{(context.IsReferenceType ? "?" : "")}>"))
        {
            WriteNewMember(writer, context, addNewLine: false, InheritDocComment);
            using (writer.BeginBlock($"public override {context.TypeName}{(context.IsReferenceType ? "?" : "")} Deserialize(global::MongoDB.Bson.Serialization.BsonDeserializationContext context, global::MongoDB.Bson.Serialization.BsonDeserializationArgs args)"))
            {
                if (context.IsReferenceType)
                {
                    using (writer.BeginBlock("if (context.Reader.CurrentBsonType is global::MongoDB.Bson.BsonType.Null)"))
                    {
                        writer.WriteLine("context.Reader.SkipValue();");
                        writer.WriteLine("return null;");
                    }
                }

                if (context.IdType is IdType.System_Half)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<float>();");
                    writer.WriteLine($"return new {context.TypeName}((Half)serializer.Deserialize(context, args));");
                }
                else if (context.IdType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<string>();");
                    writer.WriteLine($"return {context.TypeName}.Parse(serializer.Deserialize(context, args));");
                }
                else
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{context.ValueTypeCSharpTypeName}>();");
                    writer.WriteLine($"return new {context.TypeName}(serializer.Deserialize(context, args));");
                }
            }

            WriteNewMember(writer, context, addNewLine: true, InheritDocComment);
            using (writer.BeginBlock($"public override void Serialize(global::MongoDB.Bson.Serialization.BsonSerializationContext context, global::MongoDB.Bson.Serialization.BsonSerializationArgs args, {context.TypeName}{(context.IsReferenceType ? "?" : "")} value)"))
            {
                if (context.IdType is IdType.System_Half)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<float>();");
                    writer.WriteLine($"serializer.Serialize(context, args, (float)value.Value);");
                }
                else if (context.IdType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<string>();");
                    writer.WriteLine($"serializer.Serialize(context, args, value.ValueAsString);");
                }
                else
                {
                    if (context.IsReferenceType)
                    {
                        using (writer.BeginBlock($"if (value == null)"))
                        {
                            writer.WriteLine($"context.Writer.WriteNull();");
                            writer.WriteLine($"return;");
                        }
                    }

                    if (context.IsValueTypeNullable)
                    {
                        using (writer.BeginBlock($"if (value.Value == null)"))
                        {
                            writer.WriteLine($"context.Writer.WriteNull();");
                            writer.WriteLine($"return;");
                        }
                    }

                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{context.ValueTypeCSharpTypeName}>();");
                    writer.WriteLine($"serializer.Serialize(context, args, value.Value);");
                }
            }
        }
    }
}
