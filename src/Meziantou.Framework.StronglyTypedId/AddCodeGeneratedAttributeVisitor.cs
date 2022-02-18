using System.Reflection;
using Meziantou.Framework.CodeDom;

namespace Meziantou.Framework.StronglyTypedId;

internal sealed class AddCodeGeneratedAttributeVisitor : Visitor
{
    private static readonly string s_version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public override void VisitFieldDeclaration(FieldDeclaration fieldDeclaration)
    {
        AddAttribute(fieldDeclaration);
        base.VisitFieldDeclaration(fieldDeclaration);
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclaration eventFieldDeclaration)
    {
        AddAttribute(eventFieldDeclaration);
        base.VisitEventFieldDeclaration(eventFieldDeclaration);
    }

    public override void VisitMethodDeclaration(MethodDeclaration methodDeclaration)
    {
        AddAttribute(methodDeclaration);
        base.VisitMethodDeclaration(methodDeclaration);
    }

    public override void VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration)
    {
        AddAttribute(propertyDeclaration);
        base.VisitPropertyDeclaration(propertyDeclaration);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration)
    {
        AddAttribute(constructorDeclaration);
        base.VisitConstructorDeclaration(constructorDeclaration);
    }

    public override void VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration)
    {
        AddAttribute(delegateDeclaration);
        base.VisitDelegateDeclaration(delegateDeclaration);
    }

    public override void VisitEnumerationDeclaration(EnumerationDeclaration enumerationDeclaration)
    {
        AddAttribute(enumerationDeclaration);
        base.VisitEnumerationDeclaration(enumerationDeclaration);
    }

    public override void VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration)
    {
        AddAttribute(operatorDeclaration);
        base.VisitOperatorDeclaration(operatorDeclaration);
    }

    private static void AddAttribute(ICustomAttributeContainer container)
    {
        container.CustomAttributes.Add(new CustomAttribute(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute))
        {
            Arguments =
            {
                new CustomAttributeArgument(new LiteralExpression("Meziantou.Framework.StronglyTypedId")),
                new CustomAttributeArgument(new LiteralExpression(s_version)),
            },
        });
    }
}
