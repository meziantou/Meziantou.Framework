namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateMongoDBBsonSerializationConverter(CSharpGeneratedFileWriter writer, StronglyTypedIdInfo context)
    {
        if (!context.CanGenerateMongoDbConverter())
            return;

        var typeReference = GetTypeReference(context.AttributeInfo.IdType);

        using (writer.BeginBlock($"partial class {context.MongoDbConverterTypeName} : global::MongoDB.Bson.Serialization.Serializers.SerializerBase<{context.Name}>"))
        {
            WriteNewMember(writer, context, addNewLine: false);
            using (writer.BeginBlock($"public override {context.Name} Deserialize(global::MongoDB.Bson.Serialization.BsonDeserializationContext context, global::MongoDB.Bson.Serialization.BsonDeserializationArgs args)"))
            {
                if (context.AttributeInfo.IdType is IdType.System_Half)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<float>();");
                    writer.WriteLine($"return new {context.Name}((Half)serializer.Deserialize(context, args));");
                }
                else if (context.AttributeInfo.IdType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<string>();");
                    writer.WriteLine($"return {context.Name}.Parse(serializer.Deserialize(context, args));");
                }
                else
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{typeReference}>();");
                    writer.WriteLine($"return new {context.Name}(serializer.Deserialize(context, args));");
                }
            }

            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock($"public override void Serialize(global::MongoDB.Bson.Serialization.BsonSerializationContext context, global::MongoDB.Bson.Serialization.BsonSerializationArgs args, {context.Name} value)"))
            {
                if (context.AttributeInfo.IdType is IdType.System_Half)
                {
                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<float>();");
                    writer.WriteLine($"serializer.Serialize(context, args, (float)value.Value);");
                }
                else if (context.AttributeInfo.IdType is IdType.System_Int128 or IdType.System_UInt128 or IdType.System_Numerics_BigInteger)
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

                    if (IsNullable(context.AttributeInfo.IdType))
                    {
                        using (writer.BeginBlock($"if (value.Value == null)"))
                        {
                            writer.WriteLine($"context.Writer.WriteNull();");
                            writer.WriteLine($"return;");
                        }
                    }

                    writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{typeReference}>();");
                    writer.WriteLine($"serializer.Serialize(context, args, value.Value);");
                }
            }
        }
    }
}
