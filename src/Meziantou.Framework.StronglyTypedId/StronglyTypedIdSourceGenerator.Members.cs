using System.Globalization;
using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    // ISpanFormattable
    private static void GenerateTypeMembers(Compilation compilation, ClassOrStructDeclaration structDeclaration, StronglyTypedIdInfo context)
    {
        var idType = context.AttributeInfo.IdType;
        var typeReference = GetTypeReference(idType);
        var shortName = GetShortName(typeReference);

        // Field
        if (!context.IsFieldDefined())
        {
            _ = structDeclaration.AddMember(new FieldDeclaration(FieldName, typeReference) { Modifiers = Modifiers.Private | Modifiers.ReadOnly });
        }

        // Value
        if (!context.IsValueDefined())
        {
            var valuePropertyDeclaration = structDeclaration.AddMember(new PropertyDeclaration(PropertyName, typeReference) { Modifiers = Modifiers.Public });
            valuePropertyDeclaration.Getter = new PropertyAccessorDeclaration(new ReturnStatement(new MemberReferenceExpression(new ThisExpression(), FieldName)));
        }

        // ValueAsString
        if (!context.IsValueAsStringDefined())
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
        if (!context.IsCtorDefined())
        {
            var constructor = structDeclaration.AddMember(new ConstructorDeclaration { Modifiers = GetPrivateOrProtectedModifier(context) });
            var constructorArg = constructor.Arguments.Add(typeReference, "value");
            constructor.Statements = new StatementCollection { new AssignStatement(new MemberReferenceExpression(new ThisExpression(), FieldName), constructorArg) };
        }

        // From
        var fromMethod = structDeclaration.AddMember(new MethodDeclaration("From" + shortName) { Modifiers = Modifiers.Public | Modifiers.Static });
        fromMethod.ReturnType = structDeclaration;
        var fromMethodArg = fromMethod.Arguments.Add(typeReference, "value");
        fromMethod.Statements = new StatementCollection { new ReturnStatement(new NewObjectExpression(structDeclaration, fromMethodArg)) };

        // ToString
        if (!context.IsToStringDefined())
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
        if (!context.IsGetHashcodeDefined())
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

        // IStronglyTypedId (used as a 'marker interface' to locate strongly typed id's during runtime.
        structDeclaration.Implements.Add(new TypeReference("IStronglyTypedId"));

        // IEquatable<T>
        structDeclaration.Implements.Add(new TypeReference(typeof(IEquatable<>)).MakeGeneric(structDeclaration));
        if (!context.IsIEquatableEqualsDefined())
        {
            var equalsTypedMethod = structDeclaration.AddMember(new MethodDeclaration("Equals") { Modifiers = Modifiers.Public });
            equalsTypedMethod.ReturnType = typeof(bool);

            var equalsTypedMethodArg = equalsTypedMethod.Arguments.Add(structDeclaration, "other");
            equalsTypedMethod.Statements = new StatementCollection
            {
                new ReturnStatement(new BinaryExpression(BinaryOperator.Equals, CreateValuePropertyRef(), new MemberReferenceExpression(equalsTypedMethodArg, "Value"))),
            };

            if (context.IsReferenceType)
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
        if (!context.IsEqualsDefined())
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
        if (!context.IsOpEqualsDefined())
        {
            var equalsOperatorMethod = structDeclaration.AddMember(new OperatorDeclaration("==") { Modifiers = Modifiers.Public | Modifiers.Static });
            equalsOperatorMethod.ReturnType = typeof(bool);
            var equalsOperatorMethodArg1 = equalsOperatorMethod.Arguments.Add(structDeclaration, "a");
            var equalsOperatorMethodArg2 = equalsOperatorMethod.Arguments.Add(structDeclaration, "b");
            if (context.IsReferenceType)
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
        if (!context.IsOpNotEqualsDefined())
        {
            var notEqualsOperatorMethod = structDeclaration.AddMember(new OperatorDeclaration("!=") { Modifiers = Modifiers.Public | Modifiers.Static });
            notEqualsOperatorMethod.ReturnType = typeof(bool);
            var notEqualsOperatorMethodArg1 = notEqualsOperatorMethod.Arguments.Add(structDeclaration, "a");
            var notEqualsOperatorMethodArg2 = notEqualsOperatorMethod.Arguments.Add(structDeclaration, "b");
            if (context.IsReferenceType)
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
        var idTypeReference = GetTypeReference(idType);
        if (context.SupportReadOnlySpan())
        {
            // TryParse(ReadOnlySpan<char>)
            if (!context.IsTryParseDefined_ReadOnlySpan())
            {
                GenerateTryParseMethod(structDeclaration, context, idType, isReadOnlySpan: true);
            }

            // Parse(ReadOnlySpan<char>)
            if (!context.IsParseDefined_Span())
            {
                var parseMethod = structDeclaration.AddMember(new MethodDeclaration("Parse") { Modifiers = Modifiers.Public | Modifiers.Static });
                parseMethod.ReturnType = structDeclaration;
                var valueArg = parseMethod.AddArgument("value", typeof(ReadOnlySpan<char>));
                parseMethod.Statements = new StatementCollection();
                var result = parseMethod.Statements.Add(new VariableDeclarationStatement("result", structDeclaration));
                if (context.IsReferenceType)
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
        }

        // Parse
        {
            var parseMethod = structDeclaration.AddMember(new MethodDeclaration("Parse") { Modifiers = Modifiers.Public | Modifiers.Static });
            parseMethod.ReturnType = structDeclaration;
            var valueArg = parseMethod.AddArgument("value", typeof(string));
            parseMethod.Statements = new StatementCollection();
            var result = parseMethod.Statements.Add(new VariableDeclarationStatement("result", structDeclaration));
            if (context.IsReferenceType)
            {
                result.Type = result.Type?.MakeNullable();
            }

            parseMethod.Statements.Add(new ConditionStatement
            {
                Condition = new MemberReferenceExpression(structDeclaration, "TryParse").InvokeMethod(valueArg, new MethodInvokeArgumentExpression(result) { Direction = Direction.Out }),
                TrueStatements = new ReturnStatement(result),
                FalseStatements = new ThrowStatement(new NewObjectExpression(typeof(FormatException), Expression.Add("Value '", valueArg, "' is not valid"))),
            });
        }

        // TryParse
        if (!context.IsTryParseDefined_String())
        {
            GenerateTryParseMethod(structDeclaration, context, idType, isReadOnlySpan: false);
        }

        if (context.CanUseStaticInterface())
        {
            // ISpanParsable
            if (context.SupportReadOnlySpan() && compilation.GetTypeByMetadataName("System.ISpanParsable`1") != null)
            {
                structDeclaration.Implements.Add(new TypeReference("System.ISpanParsable").MakeGeneric(structDeclaration));

                // TryParse
                {
                    var tryParseMethod = structDeclaration.AddMember(new MethodDeclaration("TryParse") { Modifiers = Modifiers.Static });
                    tryParseMethod.PrivateImplementationType = new TypeReference("System.ISpanParsable").MakeGeneric(structDeclaration);
                    tryParseMethod.ReturnType = typeof(bool);
                    var valueArg = tryParseMethod.AddArgument("value", typeof(ReadOnlySpan<char>));
                    var providerArg = tryParseMethod.AddArgument("provider", new TypeReference(typeof(IFormatProvider)).MakeNullable());
                    var resultArg = tryParseMethod.AddArgument("result", structDeclaration, Direction.Out);
                    if (context.IsReferenceType)
                    {
                        resultArg.Type = resultArg.Type?.MakeNullable();
                    }

                    if (context.Compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") != null)
                    {
                        resultArg.CustomAttributes.Add(new CustomAttribute(typeof(NotNullWhenAttribute)) { Arguments = { new CustomAttributeArgument(Expression.True()) } });
                    }

                    tryParseMethod.Statements =
                            new ReturnStatement(
                                new MethodInvokeExpression(
                                    new MemberReferenceExpression(structDeclaration, "TryParse"),
                                    valueArg,
                                    new MethodInvokeArgumentExpression(resultArg, Direction.Out)));
                }

                // Parse
                {
                    var parseMethod = structDeclaration.AddMember(new MethodDeclaration("Parse") { Modifiers = Modifiers.Static });
                    parseMethod.PrivateImplementationType = new TypeReference("System.ISpanParsable").MakeGeneric(structDeclaration);
                    parseMethod.ReturnType = structDeclaration;
                    var valueArg = parseMethod.AddArgument("value", typeof(ReadOnlySpan<char>));
                    var providerArg = parseMethod.AddArgument("provider", new TypeReference(typeof(IFormatProvider)).MakeNullable());
                    parseMethod.Statements = new StatementCollection();
                    var result = parseMethod.Statements.Add(new VariableDeclarationStatement("result", structDeclaration));
                    parseMethod.Statements =
                            new ReturnStatement(
                                new MethodInvokeExpression(
                                    new MemberReferenceExpression(structDeclaration, "Parse"),
                                    valueArg));
                }
            }

            // IParsable
            if (compilation.GetTypeByMetadataName("System.IParsable`1") != null)
            {
                structDeclaration.Implements.Add(new TypeReference("System.IParsable").MakeGeneric(structDeclaration));

                // TryParse
                {
                    var tryParseMethod = structDeclaration.AddMember(new MethodDeclaration("TryParse") { Modifiers = Modifiers.Static });
                    tryParseMethod.PrivateImplementationType = new TypeReference("System.IParsable").MakeGeneric(structDeclaration);
                    tryParseMethod.ReturnType = typeof(bool);
                    var valueArg = tryParseMethod.AddArgument("value", new TypeReference(typeof(string)).MakeNullable());
                    var providerArg = tryParseMethod.AddArgument("provider", new TypeReference(typeof(IFormatProvider)).MakeNullable());
                    var resultArg = tryParseMethod.AddArgument("result", structDeclaration, Direction.Out);
                    if (context.IsReferenceType)
                    {
                        resultArg.Type = resultArg.Type?.MakeNullable();
                    }

                    if (context.Compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") != null)
                    {
                        resultArg.CustomAttributes.Add(new CustomAttribute(typeof(NotNullWhenAttribute)) { Arguments = { new CustomAttributeArgument(Expression.True()) } });
                    }

                    tryParseMethod.Statements =
                            new ReturnStatement(
                                new MethodInvokeExpression(
                                    new MemberReferenceExpression(structDeclaration, "TryParse"),
                                    valueArg,
                                    new MethodInvokeArgumentExpression(resultArg, Direction.Out)));
                }

                // Parse
                {
                    var parseMethod = structDeclaration.AddMember(new MethodDeclaration("Parse") { Modifiers = Modifiers.Static });
                    parseMethod.PrivateImplementationType = new TypeReference("System.IParsable").MakeGeneric(structDeclaration);
                    parseMethod.ReturnType = structDeclaration;
                    var valueArg = parseMethod.AddArgument("value", typeof(string));
                    var providerArg = parseMethod.AddArgument("provider", new TypeReference(typeof(IFormatProvider)).MakeNullable());
                    parseMethod.Statements = new StatementCollection();
                    var result = parseMethod.Statements.Add(new VariableDeclarationStatement("result", structDeclaration));
                    parseMethod.Statements =
                            new ReturnStatement(
                                new MethodInvokeExpression(
                                    new MemberReferenceExpression(structDeclaration, "Parse"),
                                    valueArg));
                }
            }
        }

        static void GenerateTryParseMethod(ClassOrStructDeclaration structDeclaration, StronglyTypedIdInfo context, IdType idType, bool isReadOnlySpan)
        {
            var tryParseMethod = structDeclaration.AddMember(new MethodDeclaration("TryParse") { Modifiers = Modifiers.Public | Modifiers.Static });
            tryParseMethod.ReturnType = typeof(bool);
            var valueArg = tryParseMethod.AddArgument("value", isReadOnlySpan ? typeof(ReadOnlySpan<char>) : new TypeReference(typeof(string)).MakeNullable());
            var resultArg = tryParseMethod.AddArgument("result", structDeclaration, Direction.Out);
            if (context.IsReferenceType)
            {
                resultArg.Type = resultArg.Type?.MakeNullable();
            }

            if (context.Compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") != null)
            {
                resultArg.CustomAttributes.Add(new CustomAttribute(typeof(System.Diagnostics.CodeAnalysis.NotNullWhenAttribute)) { Arguments = { new CustomAttributeArgument(Expression.True()) } });
            }

            if (!isReadOnlySpan && context.SupportReadOnlySpan())
            {
                // delegate to ReadOnlySpan<char> overload
                tryParseMethod.Statements = new ConditionStatement()
                {
                    Condition = new BinaryExpression(BinaryOperator.Equals, valueArg, Expression.Null()),
                    TrueStatements = new StatementCollection
                    {
                        new AssignStatement(resultArg, new DefaultValueExpression(structDeclaration)),
                        new ReturnStatement(Expression.False()),
                    },
                    FalseStatements = new StatementCollection
                    {
                        new ReturnStatement(
                            new MethodInvokeExpression(
                                new MemberReferenceExpression(structDeclaration, "TryParse"),
                                new MethodInvokeExpression(new MemberReferenceExpression(typeof(MemoryExtensions), "AsSpan"), valueArg),
                                new MethodInvokeArgumentExpression(resultArg, Direction.Out))),
                    },
                };

                return;
            }

            if (idType == IdType.System_String)
            {
                tryParseMethod.Statements = new StatementCollection()
                {
                    new AssignStatement(resultArg, new NewObjectExpression(structDeclaration, isReadOnlySpan ? new NewObjectExpression(typeof(string), valueArg) : valueArg)),
                    new ReturnStatement(Expression.True()),
                };

                if (!isReadOnlySpan)
                {
                    tryParseMethod.Statements.Insert(0, new ConditionStatement()
                    {
                        Condition = new BinaryExpression(BinaryOperator.Equals, valueArg, Expression.Null()),
                        TrueStatements = new StatementCollection
                        {
                            new AssignStatement(resultArg, new DefaultValueExpression(structDeclaration)),
                            new ReturnStatement(Expression.False()),
                        },
                    });
                }
            }
            else
            {
                tryParseMethod.Statements = new StatementCollection();
                var result = tryParseMethod.Statements.Add(new VariableDeclarationStatement("id", GetTypeReference(idType)));
                tryParseMethod.Statements.Add(new ConditionStatement
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
 