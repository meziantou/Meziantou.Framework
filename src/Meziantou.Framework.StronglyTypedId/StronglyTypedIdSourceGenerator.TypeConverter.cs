using System;
using System.Globalization;
using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.StronglyTypedId;

public partial class StronglyTypedIdSourceGenerator
{
    private static void GenerateTypeConverter(ClassOrStructDeclaration typeDeclaration, Compilation compilation, IdType idType)
    {
        if (!IsTypeDefined(compilation, "System.ComponentModel.TypeConverter"))
            return;

        var converter = typeDeclaration.AddType(new ClassDeclaration(typeDeclaration.Name + "TypeConverter") { Modifiers = Modifiers.Private | Modifiers.Partial });
        typeDeclaration.CustomAttributes.Add(new CustomAttribute(new TypeReference("System.ComponentModel.TypeConverterAttribute")) { Arguments = { new CustomAttributeArgument(new TypeOfExpression(converter)) } });
        converter.BaseType = new TypeReference("System.ComponentModel.TypeConverter");

        // public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            var method = converter.AddMember(new MethodDeclaration("CanConvertFrom") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = typeof(bool);
            _ = method.AddArgument("context", new TypeReference("System.ComponentModel.ITypeDescriptorContext"));
            var typeArg = method.AddArgument("sourceType", typeof(Type));

            method.Statements = new ReturnStatement(
                Expression.Or(
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(typeof(string))),
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(GetTypeReference(idType))),
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(typeDeclaration))));
        }

        // public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var method = converter.AddMember(new MethodDeclaration("ConvertFrom") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = new TypeReference(typeof(object)).MakeNullable();
            _ = method.AddArgument("context", new TypeReference("System.ComponentModel.ITypeDescriptorContext"));
            _ = method.AddArgument("culture", new TypeReference(typeof(CultureInfo)));
            var valueArg = method.AddArgument("value", typeof(object));
            method.Statements = new StatementCollection
                {
                    new ConditionStatement
                    {
                        Condition = Expression.EqualsNull(valueArg),
                        TrueStatements = new ReturnStatement(new DefaultValueExpression(GetTypeReference(idType))),
                        FalseStatements = new ConditionStatement
                        {
                            Condition = new IsInstanceOfTypeExpression(valueArg, GetTypeReference(idType)),
                            TrueStatements = new ReturnStatement(new MemberReferenceExpression(typeDeclaration, "From" + GetShortName(GetTypeReference(idType))).InvokeMethod(new CastExpression(valueArg, GetTypeReference(idType)))),
                            FalseStatements = new ConditionStatement
                            {
                                Condition = new IsInstanceOfTypeExpression(valueArg, typeof(string)),
                                TrueStatements = new ReturnStatement(new MemberReferenceExpression(typeDeclaration, "Parse").InvokeMethod(new CastExpression(valueArg, typeof(string)))),
                                FalseStatements = new ThrowStatement(new NewObjectExpression(typeof(ArgumentException), Expression.Add("Cannot convert '", valueArg, "' to " + typeDeclaration.Name))),
                            },
                        },
                    },
                };
        }

        // public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            var method = converter.AddMember(new MethodDeclaration("CanConvertTo") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = typeof(bool);
            _ = method.AddArgument("context", new TypeReference("System.ComponentModel.ITypeDescriptorContext"));
            var typeArg = method.AddArgument("destinationType", typeof(Type));
            method.Statements = new ReturnStatement(
                Expression.Or(
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(GetTypeReference(idType))),
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(typeDeclaration)),
                    new BinaryExpression(BinaryOperator.Equals, typeArg, new TypeOfExpression(typeof(string)))));
        }

        // public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            var method = converter.AddMember(new MethodDeclaration("ConvertTo") { Modifiers = Modifiers.Public | Modifiers.Override });
            method.ReturnType = typeof(object);
            _ = method.AddArgument("context", new TypeReference("System.ComponentModel.ITypeDescriptorContext"));
            _ = method.AddArgument("culture", new TypeReference(typeof(CultureInfo)));
            var valueArg = method.AddArgument("value", typeof(object));
            var destinationTypeArg = method.AddArgument("destinationType", typeof(Type));
            method.Statements = new StatementCollection()
                {
                    new ConditionStatement
                    {
                        Condition = new BinaryExpression(BinaryOperator.Equals, destinationTypeArg, new TypeOfExpression(typeof(string))),
                        TrueStatements = new ReturnStatement(new CastExpression(valueArg, typeDeclaration).Member("ValueAsString")),
                    },
                    new ConditionStatement
                    {
                        Condition = new BinaryExpression(BinaryOperator.Equals, destinationTypeArg, new TypeOfExpression(typeDeclaration)),
                        TrueStatements = new ReturnStatement(valueArg),
                    },
                    new ConditionStatement
                    {
                        Condition = new BinaryExpression(BinaryOperator.Equals, destinationTypeArg, new TypeOfExpression(GetTypeReference(idType))),
                        TrueStatements = new ReturnStatement(new CastExpression(valueArg, typeDeclaration).Member("Value")),
                    },
                    new ThrowStatement(new NewObjectExpression(typeof(ArgumentException), Expression.Add("Cannot convert '", valueArg, "' to '", destinationTypeArg, "'"))),
                };
        }
    }
}
