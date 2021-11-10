using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId
{
    public partial class StronglyTypedIdSourceGenerator
    {
        private static void GenerateMongoDBBsonSerializationConverter(ClassOrStructDeclaration structDeclaration, Compilation compilation, StronglyTypedIdInfo stronglyTypedType)
        {
            if (!IsTypeDefined(compilation, "MongoDB.Bson.Serialization.Serializers.SerializerBase`1"))
                return;

            var converter = structDeclaration.AddType(new ClassDeclaration(structDeclaration.Name + "MongoDBBsonSerializer") { Modifiers = Modifiers.Private | Modifiers.Partial });
            structDeclaration.CustomAttributes.Add(new CustomAttribute(new TypeReference("MongoDB.Bson.Serialization.Attributes.BsonSerializerAttribute")) { Arguments = { new CustomAttributeArgument(new TypeOfExpression(converter)) } });
            converter.BaseType = new TypeReference("MongoDB.Bson.Serialization.Serializers.SerializerBase").MakeGeneric(structDeclaration);

            var typeReference = GetTypeReference(stronglyTypedType.AttributeInfo.IdType);
            // public override A Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
            {
                var deserialize = converter.AddMember(new MethodDeclaration("Deserialize") { Modifiers = Modifiers.Public | Modifiers.Override });
                deserialize.ReturnType = structDeclaration;
                var context = deserialize.AddArgument("context", new TypeReference("MongoDB.Bson.Serialization.BsonDeserializationContext"));
                var args = deserialize.AddArgument("args", new TypeReference("MongoDB.Bson.Serialization.BsonDeserializationArgs"));

                deserialize.Statements = new StatementCollection();
                var serializer = deserialize.Statements.Add(new VariableDeclarationStatement("serializer", new TypeReference("MongoDB.Bson.Serialization.IBsonSerializer").MakeGeneric(typeReference)));
                serializer.InitExpression = new MemberReferenceExpression(new TypeReference("MongoDB.Bson.Serialization.BsonSerializer"), "LookupSerializer").InvokeMethod(new[] { typeReference });

                deserialize.Statements.Add(new ReturnStatement(new NewObjectExpression(structDeclaration, serializer.Member("Deserialize").InvokeMethod(context, args))));
            }

            // public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, A value)
            {
                var serialize = converter.AddMember(new MethodDeclaration("Serialize") { Modifiers = Modifiers.Public | Modifiers.Override });
                var context = serialize.AddArgument("context", new TypeReference("MongoDB.Bson.Serialization.BsonSerializationContext"));
                _ = serialize.AddArgument("args", new TypeReference("MongoDB.Bson.Serialization.BsonSerializationArgs"));
                var value = serialize.AddArgument("value", new TypeReference(structDeclaration));

                serialize.Statements = new StatementCollection();
                var serializer = serialize.Statements.Add(new VariableDeclarationStatement("serializer", new TypeReference("MongoDB.Bson.Serialization.IBsonSerializer").MakeGeneric(typeReference)));
                serializer.InitExpression = new MemberReferenceExpression(new TypeReference("MongoDB.Bson.Serialization.BsonSerializer"), "LookupSerializer").InvokeMethod(new[] { typeReference });

                serialize.Statements.Add(new MemberReferenceExpression(new TypeReference("MongoDB.Bson.Serialization.IBsonSerializerExtensions"), "Serialize").InvokeMethod(serializer, context, new MemberReferenceExpression(value, PropertyName)));
            }
        }
    }
}
