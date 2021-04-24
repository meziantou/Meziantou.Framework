using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId
{
    partial class StronglyTypedIdSourceGenerator
    {
        private static void GenerateTypeMembers(Compilation compilation, ClassOrStructDeclaration structDeclaration, StronglyTypedType stronglyTypedStruct)
        {
            var idType = stronglyTypedStruct.AttributeInfo.IdType;
            var typeReference = GetTypeReference(idType);
            var shortName = GetShortName(typeReference);

            // Field
            if (!stronglyTypedStruct.IsFieldDefined())
            {
                _ = structDeclaration.AddMember(new FieldDeclaration(FieldName, typeReference) { Modifiers = Modifiers.Private | Modifiers.ReadOnly });
            }

            // Value
            if (!stronglyTypedStruct.IsValueDefined())
            {
                var valuePropertyDeclaration = structDeclaration.AddMember(new PropertyDeclaration(PropertyName, typeReference) { Modifiers = Modifiers.Public });
                valuePropertyDeclaration.Getter = new PropertyAccessorDeclaration(new ReturnStatement(new MemberReferenceExpression(new ThisExpression(), FieldName)));
            }

            // ValueAsString
            if (!stronglyTypedStruct.IsValueAsStringDefined())
            {
                var valueAsStringProperty = structDeclaration.AddMember(new PropertyDeclaration(PropertyAsStringName, typeof(string)) { Modifiers = Modifiers.Public });
                valueAsStringProperty.Getter = new PropertyAccessorDeclaration(new ReturnStatement(ValueToStringExpression()));
            }

            MemberReferenceExpression CreateValuePropertyRef() => new(new ThisExpression(), PropertyName);
            MemberReferenceExpression CreateValueAsStringPropertyRef() => new(new ThisExpression(), PropertyAsStringName);

            Expression ValueToStringExpression()
            {
                var valueProperty = CreateValuePropertyRef();
                if (idType == IdType.System_String)
                    return valueProperty;

                if (idType == IdType.System_Boolean || idType == IdType.System_Guid)
                    return valueProperty.Member("ToString").InvokeMethod();

                if (idType == IdType.System_DateTime)
                    return valueProperty.Member("ToString").InvokeMethod("o", Expression.Member(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)));

                if (idType == IdType.System_DateTimeOffset)
                    return valueProperty.Member("UtcDateTime").Member("ToString").InvokeMethod("o", Expression.Member(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)));

                return valueProperty.Member("ToString").InvokeMethod(Expression.Member(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)));
            }

            // ctor
            if (!stronglyTypedStruct.IsCtorDefined())
            {
                var constructor = structDeclaration.AddMember(new ConstructorDeclaration { Modifiers = Modifiers.Private });
                var constructorArg = constructor.Arguments.Add(typeReference, "value");
                constructor.Statements = new StatementCollection { new AssignStatement(new MemberReferenceExpression(new ThisExpression(), FieldName), constructorArg) };
            }

            // From
            var fromMethod = structDeclaration.AddMember(new MethodDeclaration("From" + shortName) { Modifiers = Modifiers.Public | Modifiers.Static });
            fromMethod.ReturnType = structDeclaration;
            var fromMethodArg = fromMethod.Arguments.Add(typeReference, "value");
            fromMethod.Statements = new StatementCollection { new ReturnStatement(new NewObjectExpression(structDeclaration, fromMethodArg)) };

            // ToString
            if (!stronglyTypedStruct.IsToStringDefined())
            {
                var toStringMethod = structDeclaration.AddMember(new MethodDeclaration("ToString") { Modifiers = Modifiers.Public | Modifiers.Override });
                toStringMethod.ReturnType = typeof(string);
                if (IsNullable(idType))
                {
                    toStringMethod.Statements = new ConditionStatement
                    {
                        Condition = Expression.EqualsNull(CreateValuePropertyRef()),
                        TrueStatements = new ReturnStatement(structDeclaration.Name + " { Value = <null> }"),
                        FalseStatements = new ReturnStatement(Expression.Add(structDeclaration.Name + " { Value = ", CreateValueAsStringPropertyRef(), " }")),
                    };
                }
                else
                {
                    toStringMethod.Statements = new ReturnStatement(Expression.Add(structDeclaration.Name + " { Value = ", CreateValueAsStringPropertyRef(), " }"));
                }
            }

            // if Guid => New
            if (idType == IdType.System_Guid)
            {
                var newMethod = structDeclaration.AddMember(new MethodDeclaration("New") { Modifiers = Modifiers.Public | Modifiers.Static });
                newMethod.ReturnType = structDeclaration;
                newMethod.Statements = new StatementCollection { new ReturnStatement(new NewObjectExpression(structDeclaration, new MethodInvokeExpression(new MemberReferenceExpression(typeof(Guid), nameof(Guid.NewGuid))))) };
            }

            // GetHashCode
            if (!stronglyTypedStruct.IsGetHashcodeDefined())
            {
                var getHashCodeMethod = structDeclaration.AddMember(new MethodDeclaration("GetHashCode") { Modifiers = Modifiers.Public | Modifiers.Override });
                getHashCodeMethod.ReturnType = typeof(int);
                if (IsNullable(idType))
                {
                    getHashCodeMethod.Statements = new ConditionStatement
                    {
                        Condition = Expression.EqualsNull(CreateValuePropertyRef()),
                        TrueStatements = new ReturnStatement(0),
                        FalseStatements = new ReturnStatement(CreateValuePropertyRef().Member("GetHashCode").InvokeMethod()),
                    };
                }
                else
                {
                    getHashCodeMethod.Statements = new ReturnStatement(CreateValuePropertyRef().Member("GetHashCode").InvokeMethod());
                }
            }

            // IEquatable<T>
            structDeclaration.Implements.Add(new TypeReference(typeof(IEquatable<>)).MakeGeneric(structDeclaration));
            if (!stronglyTypedStruct.IsIEquatableEqualsDefined())
            {
                var equalsTypedMethod = structDeclaration.AddMember(new MethodDeclaration("Equals") { Modifiers = Modifiers.Public });
                equalsTypedMethod.ReturnType = typeof(bool);

                var equalsTypedMethodArg = equalsTypedMethod.Arguments.Add(structDeclaration, "other");
                equalsTypedMethod.Statements = new StatementCollection
                {
                    new ReturnStatement(new BinaryExpression(BinaryOperator.Equals, CreateValuePropertyRef(), new MemberReferenceExpression(equalsTypedMethodArg, "Value"))),
                };

                if (stronglyTypedStruct.IsReferenceType)
                {
                    equalsTypedMethodArg.Type = equalsTypedMethodArg.Type?.MakeNullable();
                    equalsTypedMethod.Statements.Insert(0, new ConditionStatement
                    {
                        Condition = Expression.ReferenceEqualsNull(equalsTypedMethodArg),
                        TrueStatements = new ReturnStatement(Expression.False()),
                    });
                }
            }

            // Equals
            if (!stronglyTypedStruct.IsEqualsDefined())
            {
                var equalsMethod = structDeclaration.AddMember(new MethodDeclaration("Equals") { Modifiers = Modifiers.Public | Modifiers.Override });
                equalsMethod.ReturnType = typeof(bool);
                var equalsMethodArg = equalsMethod.Arguments.Add(new TypeReference(typeof(object)).MakeNullable(), "other");
                equalsMethod.Statements = new StatementCollection
                {
                    new ConditionStatement()
                    {
                        Condition = new IsInstanceOfTypeExpression(equalsMethodArg, structDeclaration),
                        TrueStatements = new ReturnStatement(new MethodInvokeExpression(new ThisExpression().Member("Equals"), new CastExpression(equalsMethodArg, structDeclaration))),
                        FalseStatements = new ReturnStatement(Expression.False()),
                    },
                };
            }

            // Operator ==
            if (!stronglyTypedStruct.IsOpEqualsDefined())
            {
                var equalsOperatorMethod = structDeclaration.AddMember(new OperatorDeclaration("==") { Modifiers = Modifiers.Public | Modifiers.Static });
                equalsOperatorMethod.ReturnType = typeof(bool);
                var equalsOperatorMethodArg1 = equalsOperatorMethod.Arguments.Add(structDeclaration, "a");
                var equalsOperatorMethodArg2 = equalsOperatorMethod.Arguments.Add(structDeclaration, "b");
                if (stronglyTypedStruct.IsReferenceType)
                {
                    equalsOperatorMethodArg1.Type = equalsOperatorMethodArg1.Type?.MakeNullable();
                    equalsOperatorMethodArg2.Type = equalsOperatorMethodArg2.Type?.MakeNullable();
                }

                equalsOperatorMethod.Statements.Add(
                    new ReturnStatement(
                        new MethodInvokeExpression(
                            new MemberReferenceExpression(new MemberReferenceExpression(new TypeReference(typeof(EqualityComparer<>)).MakeGeneric(structDeclaration), "Default"), "Equals"),
                            equalsOperatorMethodArg1,
                            equalsOperatorMethodArg2)));
            }

            // Operator !=
            if (!stronglyTypedStruct.IsOpNotEqualsDefined())
            {
                var notEqualsOperatorMethod = structDeclaration.AddMember(new OperatorDeclaration("!=") { Modifiers = Modifiers.Public | Modifiers.Static });
                notEqualsOperatorMethod.ReturnType = typeof(bool);
                var notEqualsOperatorMethodArg1 = notEqualsOperatorMethod.Arguments.Add(structDeclaration, "a");
                var notEqualsOperatorMethodArg2 = notEqualsOperatorMethod.Arguments.Add(structDeclaration, "b");
                if (stronglyTypedStruct.IsReferenceType)
                {
                    notEqualsOperatorMethodArg1.Type = notEqualsOperatorMethodArg1.Type?.MakeNullable();
                    notEqualsOperatorMethodArg2.Type = notEqualsOperatorMethodArg2.Type?.MakeNullable();
                }

                notEqualsOperatorMethod.Statements.Add(
                    new ReturnStatement(
                        new UnaryExpression(UnaryOperator.Not,
                            new BinaryExpression(BinaryOperator.Equals, notEqualsOperatorMethodArg1, notEqualsOperatorMethodArg2))));
            }

            // Parse / TryParse
            // Check if we can add Span<char> overloads
            var valueTypes = new List<TypeReference>() { new TypeReference(typeof(string)) };
            var readOnlySpan = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            var charSymbol = compilation.GetTypeByMetadataName("System.Char");
            var idTypeReference = GetTypeReference(idType);
            if (readOnlySpan != null && charSymbol != null && idTypeReference.TypeName != null)
            {
                if (compilation.GetTypeByMetadataName(idTypeReference.TypeName)?.GetMembers("TryParse").OfType<IMethodSymbol>()
                        .Any(m => m.Parameters.Length > 0 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, readOnlySpan.Construct(charSymbol))) == true)
                {
                    valueTypes.Add(new TypeReference(typeof(ReadOnlySpan<char>)));
                }
            }

            foreach (var valueType in valueTypes)
            {
                // Parse
                if (!stronglyTypedStruct.IsParseDefined())
                {
                    var parseMethod = structDeclaration.AddMember(new MethodDeclaration("Parse") { Modifiers = Modifiers.Public | Modifiers.Static });
                    parseMethod.ReturnType = structDeclaration;
                    var valueArg = parseMethod.AddArgument("value", valueType);
                    parseMethod.Statements = new StatementCollection();
                    var result = parseMethod.Statements.Add(new VariableDeclarationStatement("result", structDeclaration));
                    if (stronglyTypedStruct.IsReferenceType)
                    {
                        result.Type = result.Type?.MakeNullable();
                    }

                    parseMethod.Statements.Add(new ConditionStatement
                    {
                        Condition = new MemberReferenceExpression(structDeclaration, "TryParse").InvokeMethod(valueArg, new MethodInvokeArgumentExpression(result) { Direction = Direction.Out }),
                        TrueStatements = new ReturnStatement(result),
                        FalseStatements = new ThrowStatement(new NewObjectExpression(typeof(FormatException), Expression.Add("Value '", valueArg.Member("ToString").InvokeMethod(), "' is not valid"))),
                    });
                }

                // TryParse
                if (!stronglyTypedStruct.IsTryParseDefined())
                {
                    var parseMethod = structDeclaration.AddMember(new MethodDeclaration("TryParse") { Modifiers = Modifiers.Public | Modifiers.Static });
                    parseMethod.ReturnType = typeof(bool);
                    var valueArg = parseMethod.AddArgument("value", valueType);
                    var resultArg = parseMethod.AddArgument("result", structDeclaration, Direction.Out);
                    if (stronglyTypedStruct.IsReferenceType)
                    {
                        resultArg.Type = resultArg.Type?.MakeNullable();
                    }

                    if (compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") != null)
                    {
                        resultArg.CustomAttributes.Add(new CustomAttribute(typeof(System.Diagnostics.CodeAnalysis.NotNullWhenAttribute)) { Arguments = { new CustomAttributeArgument(LiteralExpression.True()) } });
                    }

                    if (idType == IdType.System_String)
                    {
                        parseMethod.Statements = new StatementCollection()
                        {
                            new AssignStatement(resultArg, new NewObjectExpression(structDeclaration, valueArg)),
                            new ReturnStatement(Expression.True()),
                        };
                    }
                    else
                    {
                        parseMethod.Statements = new StatementCollection();
                        var result = parseMethod.Statements.Add(new VariableDeclarationStatement("id", GetTypeReference(idType)));
                        parseMethod.Statements.Add(new ConditionStatement
                        {
                            Condition = CreateTryParseExpression(),
                            TrueStatements = new StatementCollection
                            {
                                new AssignStatement(resultArg, new NewObjectExpression(structDeclaration, result)),
                                new ReturnStatement(Expression.True()),
                            },
                            FalseStatements = new StatementCollection
                            {
                                new AssignStatement(resultArg, new DefaultValueExpression(structDeclaration)),
                                new ReturnStatement(Expression.False()),
                            },
                        });

                        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "Less readable")]
                        Expression CreateTryParseExpression()
                        {
                            switch (idType)
                            {
                                case IdType.System_Boolean:
                                    return new MemberReferenceExpression(GetTypeReference(idType), "TryParse").InvokeMethod(
                                        valueArg,
                                        new MethodInvokeArgumentExpression(result) { Direction = Direction.Out });

                                case IdType.System_DateTime:
                                case IdType.System_DateTimeOffset:
                                    return new MemberReferenceExpression(GetTypeReference(idType), "TryParse").InvokeMethod(
                                        valueArg,
                                        new MemberReferenceExpression(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)),
                                        new MemberReferenceExpression(typeof(DateTimeStyles), nameof(DateTimeStyles.AdjustToUniversal)),
                                        new MethodInvokeArgumentExpression(result) { Direction = Direction.Out });

                                case IdType.System_Guid:
                                    return new MemberReferenceExpression(GetTypeReference(idType), "TryParse").InvokeMethod(
                                        valueArg,
                                        new MethodInvokeArgumentExpression(result) { Direction = Direction.Out });

                                case IdType.System_Decimal:
                                case IdType.System_Double:
                                case IdType.System_Single:
                                case IdType.System_Byte:
                                case IdType.System_SByte:
                                case IdType.System_Int16:
                                case IdType.System_Int32:
                                case IdType.System_Int64:
                                case IdType.System_UInt16:
                                case IdType.System_UInt32:
                                case IdType.System_UInt64:
                                    return new MemberReferenceExpression(GetTypeReference(idType), "TryParse").InvokeMethod(
                                        valueArg,
                                        new MemberReferenceExpression(typeof(NumberStyles), nameof(NumberStyles.Any)),
                                        new MemberReferenceExpression(typeof(CultureInfo), nameof(CultureInfo.InvariantCulture)),
                                        new MethodInvokeArgumentExpression(result) { Direction = Direction.Out });

                                default:
                                    throw new InvalidOperationException("Type not supported");
                            }
                        }
                    }

                }
            }
        }
    }
}
