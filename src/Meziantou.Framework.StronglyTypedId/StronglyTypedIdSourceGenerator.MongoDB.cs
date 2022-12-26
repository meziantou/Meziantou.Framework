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
                writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{typeReference}>();");
                writer.WriteLine($"return new {context.Name}(serializer.Deserialize(context, args));");
            }

            WriteNewMember(writer, context, addNewLine: true);
            using (writer.BeginBlock($"public override void Serialize(global::MongoDB.Bson.Serialization.BsonSerializationContext context, global::MongoDB.Bson.Serialization.BsonSerializationArgs args, {context.Name} value)"))
            {
                writer.WriteLine($"var serializer = global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<{typeReference}>();");
                writer.WriteLine($"serializer.Serialize(context, args, value.Value);");
            }
        }
    }
}
