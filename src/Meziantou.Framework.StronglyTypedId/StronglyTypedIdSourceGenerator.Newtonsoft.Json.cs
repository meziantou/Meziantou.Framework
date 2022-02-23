using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateNewtonsoftJsonConverter(ClassOrStructDeclaration typeDeclaration, Compilation compilation, StronglyTypedIdInfo stronglyTypedType)
    {
        if (!IsTypeDefined(compilation, "Newtonsoft.Json.JsonConverter"))
            return;

        var converter = typeDeclaration.AddType(new ClassDeclaration(typeDeclaration.Name + "NewtonsoftJsonConverter") { Modifiers = Modifiers.Private | Modifiers.Partial });
        typeDeclaration.CustomAttributes.Add(new CustomAttribute(new TypeReference("Newtonsoft.Json.JsonConverterAttribute")) { Arguments = { new CustomAttributeArgument(new TypeOfExpression(converter)) } });
        converter.BaseType = new TypeReference("Newtonsoft.Json.JsonConverter");

        // bool CanRead => true
        {
            var canRead = converter.AddMember(new PropertyDeclaration("CanRead", typeof(bool)) { Modifiers = Modifiers.Public | Modifiers.Override });
            canRead.Getter = new PropertyAccessorDeclaration()
            {
                Statements = new ReturnStatement(Expression.True()),
            };
        }

        // bool CanWrite => true
        {
            var canWrite = converter.AddMember(new PropertyDeclaration("CanWrite", typeof(bool)) { Modifiers = Modifiers.Public | Modifiers.Override });
            canWrite.Getter = new PropertyAccessorDeclaration()
            {
                Statements = new ReturnStatement(Expression.True()),
            };
        }

        // CanConvert
        {
            var method = converter.AddMember(new MethodDeclaration("CanConvert") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = typeof(bool);
            var typeArg = method.AddArgument("type", typeof(Type));

            method.Statements = new ReturnStatement(new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(typeDeclaration)));
        }

        // public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var method = converter.AddMember(new MethodDeclaration("WriteJson") { Modifiers = Modifiers.Public | Modifiers.Override });
            var writerArg = method.AddArgument("writer", new TypeReference("Newtonsoft.Json.JsonWriter"));
            var valueArg = method.AddArgument("value", new TypeReference(typeof(object)).MakeNullable());
            _ = method.AddArgument("serializer", new TypeReference("Newtonsoft.Json.JsonSerializer"));

            method.Statements = new ConditionStatement
            {
                Condition = Expression.EqualsNull(valueArg),
                TrueStatements = writerArg.Member("WriteNull").InvokeMethod(),
                FalseStatements = writerArg.Member("WriteValue").InvokeMethod(new CastExpression(valueArg, typeDeclaration).Member("Value")),
            };
        }

        // public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var method = converter.AddMember(new MethodDeclaration("ReadJson") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = new TypeReference(typeof(object)).MakeNullable();
            var readerArg = method.AddArgument("reader", new TypeReference("Newtonsoft.Json.JsonReader"));
            var typeArg = method.AddArgument("objectType", typeof(Type));
            _ = method.AddArgument("existingValue", new TypeReference(typeof(object)).MakeNullable());
            var serializerArg = method.AddArgument("serializer", new TypeReference("Newtonsoft.Json.JsonSerializer"));

            method.Statements = new StatementCollection();

            var valueVariable = method.Statements.Add(new VariableDeclarationStatement("value", new TypeReference(typeof(object)).MakeNullable(), Expression.Null()));
            method.Statements.Add(new ConditionStatement
            {
                Condition = CompareTokenType(BinaryOperator.Equals, "StartObject"),
                TrueStatements = CreateObjectParsing(),
                FalseStatements = ReadValue(),
            });

            method.Statements.Add(new ReturnStatement(valueVariable));

            BinaryExpression CompareTokenType(BinaryOperator op, string tokenType)
            {
                return new BinaryExpression(
                    op,
                    readerArg.Member("TokenType"),
                    new MemberReferenceExpression(new TypeReference("Newtonsoft.Json.JsonToken"), tokenType));
            }

            StatementCollection CreateObjectParsing()
            {
                var statements = new StatementCollection();
                var valueRead = statements.Add(new VariableDeclarationStatement("valueRead", typeof(bool), Expression.False()));
                statements.Add(ReaderRead());
                statements.Add(new WhileStatement()
                {
                    Condition = CompareTokenType(BinaryOperator.NotEquals, "EndObject"),
                    Body = new StatementCollection
                    {
                        new ConditionStatement()
                        {
                            Condition = Expression.And(
                                UnaryExpression.Not(valueRead),
                                CompareTokenType(BinaryOperator.Equals, "PropertyName"),
                                new BinaryExpression(BinaryOperator.Equals, new CastExpression(readerArg.Member("Value"), new TypeReference(typeof(string)).MakeNullable()), new LiteralExpression("Value"))),
                            TrueStatements = new StatementCollection
                            {
                                ReaderRead(),
                                ReadValue(),
                                new AssignStatement(valueRead, Expression.True()),
                                ReaderRead(),
                            },
                            FalseStatements = new StatementCollection
                            {
                                ReaderSkip(),
                                ReaderRead(),
                            },
                        },
                    },
                });
                return statements;
            }

            Statement ReaderRead()
            {
                return new MethodInvokeExpression(readerArg.Member("Read"));
            }

            Statement ReaderSkip()
            {
                return new MethodInvokeExpression(readerArg.Member("Skip"));
            }

            Statement ReadValue()
            {
                // if (targetType == Nullable<Id> or targetType is class) and token is null => return null;
                return new ConditionStatement
                {
                    Condition =
                        new BinaryExpression(
                            BinaryOperator.And,
                            CompareTokenType(BinaryOperator.Equals, "Null"),
                            stronglyTypedType.IsReferenceType ? Expression.True() : new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(new TypeReference(typeof(Nullable<>)).MakeGeneric(typeDeclaration)))),
                    TrueStatements = new StatementCollection
                    {
                        new AssignStatement(
                            valueVariable,
                            Expression.Null()),
                    },
                    FalseStatements = new StatementCollection
                    {
                        new AssignStatement(
                            valueVariable,
                            new NewObjectExpression(
                                typeDeclaration,
                                serializerArg.Member("Deserialize").InvokeMethod(new[] { GetTypeReference(stronglyTypedType.AttributeInfo.IdType) }, readerArg)))
                        {
                            NullableContext = CodeDom.NullableContext.Disable,
                        },
                    },
                };
            }
        }
    }
}
