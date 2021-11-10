using System;
using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId
{
    public partial class StronglyTypedIdSourceGenerator
    {
        private static void GenerateSystemTextJsonConverter(ClassOrStructDeclaration structDeclaration, Compilation compilation, StronglyTypedIdInfo stronglyTypedType)
        {
            if (!IsTypeDefined(compilation, "System.Text.Json.Serialization.JsonConverter`1"))
                return;

            var idType = stronglyTypedType.AttributeInfo.IdType;
            var typeReference = new TypeReference(structDeclaration);
            if (stronglyTypedType.IsReferenceType)
            {
                typeReference = typeReference.MakeNullable();
            }

            var converter = structDeclaration.AddType(new ClassDeclaration(structDeclaration.Name + "JsonConverter") { Modifiers = Modifiers.Private | Modifiers.Partial });
            structDeclaration.CustomAttributes.Add(new CustomAttribute(new TypeReference("System.Text.Json.Serialization.JsonConverterAttribute")) { Arguments = { new CustomAttributeArgument(new TypeOfExpression(converter)) } });
            converter.BaseType = new TypeReference("System.Text.Json.Serialization.JsonConverter").MakeGeneric(typeReference);

            // public abstract void Write (System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options);
            {
                var writeMethod = converter.AddMember(new MethodDeclaration("Write") { Modifiers = Modifiers.Public | Modifiers.Override });
                var writerArg = writeMethod.AddArgument("writer", new TypeReference("System.Text.Json.Utf8JsonWriter"));
                var valueArg = writeMethod.AddArgument("value", typeReference);
                var optionsArg = writeMethod.AddArgument("options", new TypeReference("System.Text.Json.JsonSerializerOptions"));

                if (stronglyTypedType.IsReferenceType)
                {
                    writeMethod.Statements = new ConditionStatement
                    {
                        Condition = Expression.EqualsNull(valueArg),
                        TrueStatements = writerArg.Member("WriteNullValue").InvokeMethod(),
                        FalseStatements = GetWriteStatement(),
                    };
                }
                else
                {
                    writeMethod.Statements = GetWriteStatement();
                }

                StatementCollection GetWriteStatement()
                {
                    if (idType == IdType.System_Boolean)
                    {
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(writerArg, "WriteBooleanValue"),
                                new MemberReferenceExpression(valueArg, "Value")),
                        };
                    }
                    else if (CanUseWriteNumberValue())
                    {
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(writerArg, "WriteNumberValue"),
                                new MemberReferenceExpression(valueArg, "Value")),
                        };
                    }
                    else if (CanUseWriteNumberValueWithCastToInt())
                    {
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(writerArg, "WriteNumberValue"),
                                new CastExpression(valueArg.Member("Value"), typeof(int))),
                        };
                    }
                    else if (CanUseWriteNumberValueWithCastToUInt())
                    {
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(writerArg, "WriteNumberValue"),
                                new CastExpression(valueArg.Member("Value"), typeof(uint))),
                        };
                    }
                    else if (CanUseWriteStringValue())
                    {
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(writerArg, "WriteStringValue"),
                                new MemberReferenceExpression(valueArg, "Value")),
                        };
                    }
                    else
                    {
                        // JsonSerializer.Serialize(writer, value.Value, options)
                        return new StatementCollection
                        {
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(new TypeReference("System.Text.Json.JsonSerializer"), "Serialize"),
                                writerArg,
                                new MemberReferenceExpression(valueArg, "Value"),
                                optionsArg),
                        };
                    }
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
            {
                var readMethod = converter.AddMember(new MethodDeclaration("Read") { Modifiers = Modifiers.Public | Modifiers.Override });
                readMethod.ReturnType = typeReference;
                var readerArg = readMethod.AddArgument("reader", new TypeReference("System.Text.Json.Utf8JsonReader"), Direction.InOut);
                _ = readMethod.AddArgument("typeToConvert", typeof(Type));
                _ = readMethod.AddArgument("options", new TypeReference("System.Text.Json.JsonSerializerOptions"));

                readMethod.Statements = new StatementCollection();
                var valueVariable = readMethod.Statements.Add(new VariableDeclarationStatement("value", typeReference, new DefaultValueExpression(structDeclaration)));
                readMethod.Statements.Add(new ConditionStatement
                {
                    Condition = CompareTokenType(BinaryOperator.Equals, "StartObject"),
                    TrueStatements = CreateObjectParsing(),
                    FalseStatements = ReadValue(),
                });

                readMethod.Statements.Add(new ReturnStatement(valueVariable));

                BinaryExpression CompareTokenType(BinaryOperator op, string tokenType)
                {
                    return new BinaryExpression(
                        op,
                        readerArg.Member("TokenType"),
                        new MemberReferenceExpression(new TypeReference("System.Text.Json.JsonTokenType"), tokenType));
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
                                    readerArg.Member("ValueTextEquals").InvokeMethod(new LiteralExpression("Value"))),
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
                    return
                        new AssignStatement(
                            valueVariable,
                            new NewObjectExpression(
                                structDeclaration,
                                new MethodInvokeExpression(new MemberReferenceExpression(readerArg, "Get" + GetShortName(GetTypeReference(idType))))))
                        {
                            NullableContext = CodeDom.NullableContext.Disable,
                        };
                }
            }
        }
    }
}
