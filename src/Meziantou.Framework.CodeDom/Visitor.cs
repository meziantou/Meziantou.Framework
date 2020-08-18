using System;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class Visitor
    {
        public virtual void Visit(CodeObject? codeObject)
        {
            if (codeObject == null)
                return;

            switch (codeObject)
            {
                case AddEventHandlerStatement addEventHandlerStatement:
                    VisitAddEventHandlerStatement(addEventHandlerStatement);
                    break;

                case ArgumentReferenceExpression argumentReferenceExpression:
                    VisitArgumentReferenceExpression(argumentReferenceExpression);
                    break;

                case ArrayIndexerExpression arrayIndexerExpression:
                    VisitArrayIndexerExpression(arrayIndexerExpression);
                    break;

                case AssignStatement assignStatement:
                    VisitAssignStatement(assignStatement);
                    break;

                case AwaitExpression awaitExpression:
                    VisitAwaitExpression(awaitExpression);
                    break;

                case BaseExpression baseExpression:
                    VisitBaseExpression(baseExpression);
                    break;

                case BaseTypeParameterConstraint baseTypeParameterConstraint:
                    VisitBaseTypeParameterConstraint(baseTypeParameterConstraint);
                    break;

                case BinaryExpression binaryExpression:
                    VisitBinaryExpression(binaryExpression);
                    break;

                case CastExpression castExpression:
                    VisitCastExpression(castExpression);
                    break;

                case CatchClause catchClause:
                    VisitCatchClause(catchClause);
                    break;

                case CatchClauseCollection catchClauseCollection:
                    VisitCatchClauseCollection(catchClauseCollection);
                    break;

                case ClassDeclaration classDeclaration:
                    VisitClassDeclaration(classDeclaration);
                    break;

                case ClassTypeParameterConstraint classTypeParameterConstraint:
                    VisitClassTypeParameterConstraint(classTypeParameterConstraint);
                    break;

                case CodeObjectCollection<Expression> expressions:
                    VisitExpressions(expressions);
                    break;

                case Comment comment:
                    VisitComment(comment);
                    break;

                case CommentCollection commentCollection:
                    VisitCommentCollection(commentCollection);
                    break;

                case CommentStatement commentStatement:
                    VisitCommentStatement(commentStatement);
                    break;

                case CompilationUnit compilationUnit:
                    VisitCompilationUnit(compilationUnit);
                    break;

                case ConditionStatement conditionStatement:
                    VisitConditionStatement(conditionStatement);
                    break;

                case ConstructorBaseInitializer constructorBaseInitializer:
                    VisitConstructorBaseInitializer(constructorBaseInitializer);
                    break;

                case ConstructorDeclaration constructorDeclaration:
                    VisitConstructorDeclaration(constructorDeclaration);
                    break;

                case ConstructorParameterConstraint constructorParameterConstraint:
                    VisitConstructorParameterConstraint(constructorParameterConstraint);
                    break;

                case ConstructorThisInitializer constructorThisInitializer:
                    VisitConstructorThisInitializer(constructorThisInitializer);
                    break;

                case ConvertExpression convertExpression:
                    VisitConvertExpression(convertExpression);
                    break;

                case CustomAttribute customAttribute:
                    VisitCustomAttribute(customAttribute);
                    break;

                case CustomAttributeArgument customAttributeArgument:
                    VisitCustomAttributeArgument(customAttributeArgument);
                    break;

                case DefaultValueExpression defaultValueExpression:
                    VisitDefaultValueExpression(defaultValueExpression);
                    break;

                case DelegateDeclaration delegateDeclaration:
                    VisitDelegateDeclaration(delegateDeclaration);
                    break;

                case EnumerationDeclaration enumerationDeclaration:
                    VisitEnumerationDeclaration(enumerationDeclaration);
                    break;

                case EnumerationMember enumerationMember:
                    VisitEnumerationMember(enumerationMember);
                    break;

                case EventFieldDeclaration eventFieldDeclaration:
                    VisitEventFieldDeclaration(eventFieldDeclaration);
                    break;

                case ExitLoopStatement exitLoopStatement:
                    VisitExitLoopStatement(exitLoopStatement);
                    break;

                case ExpressionCollectionStatement expressionCollectionStatement:
                    VisitExpressionCollectionStatement(expressionCollectionStatement);
                    break;

                case ExpressionStatement expressionStatement:
                    VisitExpressionStatement(expressionStatement);
                    break;

                case FieldDeclaration fieldDeclaration:
                    VisitFieldDeclaration(fieldDeclaration);
                    break;

                case GotoNextLoopIterationStatement gotoNextLoopIterationStatement:
                    VisitGotoNextLoopIterationStatement(gotoNextLoopIterationStatement);
                    break;

                case InterfaceDeclaration interfaceDeclaration:
                    VisitInterfaceDeclaration(interfaceDeclaration);
                    break;

                case IterationStatement iterationStatement:
                    VisitIterationStatement(iterationStatement);
                    break;

                case LiteralExpression literalExpression:
                    VisitLiteralExpression(literalExpression);
                    break;

                case MemberReferenceExpression memberReferenceExpression:
                    VisitMemberReferenceExpression(memberReferenceExpression);
                    break;

                case MethodArgumentDeclaration methodArgumentDeclaration:
                    VisitMethodArgumentDeclaration(methodArgumentDeclaration);
                    break;

                case MethodDeclaration methodDeclaration:
                    VisitMethodDeclaration(methodDeclaration);
                    break;

                case MethodExitStatement methodExitStatement:
                    VisitMethodExitStatement(methodExitStatement);
                    break;

                case MethodInvokeArgumentExpression methodInvokeArgumentExpression:
                    VisitMethodInvokeArgumentExpression(methodInvokeArgumentExpression);
                    break;

                case MethodInvokeExpression methodInvokeExpression:
                    VisitMethodInvokeExpression(methodInvokeExpression);
                    break;

                case NameofExpression nameofExpression:
                    VisitNameofExpression(nameofExpression);
                    break;

                case NamespaceDeclaration namespaceDeclaration:
                    VisitNamespaceDeclaration(namespaceDeclaration);
                    break;

                case NewObjectExpression newObjectExpression:
                    VisitNewObjectExpression(newObjectExpression);
                    break;

                case OperatorDeclaration operatorDeclaration:
                    VisitOperatorDeclaration(operatorDeclaration);
                    break;

                case PropertyAccessorDeclaration propertyAccessorDeclaration:
                    VisitPropertyAccessorDeclaration(propertyAccessorDeclaration);
                    break;

                case PropertyDeclaration propertyDeclaration:
                    VisitPropertyDeclaration(propertyDeclaration);
                    break;

                case RemoveEventHandlerStatement removeEventHandlerStatement:
                    VisitRemoveEventHandlerStatement(removeEventHandlerStatement);
                    break;

                case ReturnStatement returnStatement:
                    VisitReturnStatement(returnStatement);
                    break;

                case SnippetExpression snippetExpression:
                    VisitSnippetExpression(snippetExpression);
                    break;

                case SnippetStatement snippetStatement:
                    VisitSnippetStatement(snippetStatement);
                    break;

                case StatementCollection statementCollection:
                    VisitStatementCollection(statementCollection);
                    break;

                case StructDeclaration structDeclaration:
                    VisitStructDeclaration(structDeclaration);
                    break;

                case ThisExpression thisExpression:
                    VisitThisExpression(thisExpression);
                    break;

                case ThrowStatement throwStatement:
                    VisitThrowStatement(throwStatement);
                    break;

                case TryCatchFinallyStatement tryCatchFinallyStatement:
                    VisitTryCatchFinallyStatement(tryCatchFinallyStatement);
                    break;

                case TypeOfExpression typeOfExpression:
                    VisitTypeOfExpression(typeOfExpression);
                    break;

                case TypeParameter typeParameter:
                    VisitTypeParameter(typeParameter);
                    break;

                case TypeParameterConstraintCollection typeParameterConstraintCollection:
                    VisitTypeParameterConstraintCollection(typeParameterConstraintCollection);
                    break;

                case TypeReferenceExpression typeReference:
                    VisitTypeReferenceExpression(typeReference);
                    break;

                case UnaryExpression unaryExpression:
                    VisitUnaryExpression(unaryExpression);
                    break;

                case UnmanagedTypeParameterConstraint unmanagedTypeParameterConstraint:
                    VisitUnmanagedTypeParameterConstraint(unmanagedTypeParameterConstraint);
                    break;

                case UsingDirective usingDirective:
                    VisitUsingDirective(usingDirective);
                    break;

                case UsingStatement usingStatement:
                    VisitUsingStatement(usingStatement);
                    break;

                case ValueArgumentExpression valueArgumentExpression:
                    VisitValueArgumentExpression(valueArgumentExpression);
                    break;

                case ValueTypeTypeParameterConstraint valueTypeTypeParameterConstraint:
                    VisitValueTypeTypeParameterConstraint(valueTypeTypeParameterConstraint);
                    break;

                case VariableDeclarationStatement variableDeclarationStatement:
                    VisitVariableDeclarationStatement(variableDeclarationStatement);
                    break;

                case VariableReferenceExpression variableReference:
                    VisitVariableReference(variableReference);
                    break;

                case WhileStatement whileStatement:
                    VisitWhileStatement(whileStatement);
                    break;

                case XmlComment xmlComment:
                    VisitXmlComment(xmlComment);
                    break;

                case XmlCommentCollection xmlCommentCollection:
                    VisitXmlCommentCollection(xmlCommentCollection);
                    break;

                case YieldBreakStatement yieldBreakStatement:
                    VisitYieldBreakStatement(yieldBreakStatement);
                    break;

                case YieldReturnStatement yieldReturnStatement:
                    VisitYieldReturnStatement(yieldReturnStatement);
                    break;

                case NewArrayExpression newArrayExpression:
                    VisitNewArrayExpression(newArrayExpression);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(codeObject));
            }
        }

        protected virtual void VisitNewArrayExpression(NewArrayExpression newArrayExpression)
        {
            VisitExpression(newArrayExpression);
            VisitTypeReferenceIfNotNull(newArrayExpression.Type);
            VisitCollection(newArrayExpression.Arguments);
        }

        public virtual void VisitTypeParameterConstraintCollection(TypeParameterConstraintCollection typeParameterConstraintCollection)
        {
            VisitCollection(typeParameterConstraintCollection);
        }

        public virtual void VisitExpressionCollectionStatement(ExpressionCollectionStatement expressionCollectionStatement)
        {
            VisitStatement(expressionCollectionStatement);
            foreach (var item in expressionCollectionStatement)
            {
                Visit(item);
            }
        }

        public virtual void VisitXmlCommentCollection(XmlCommentCollection xmlCommentCollection)
        {
            VisitCollection(xmlCommentCollection);
        }

        public virtual void VisitCommentCollection(CommentCollection commentCollection)
        {
            VisitCollection(commentCollection);
        }

        public virtual void VisitYieldReturnStatement(YieldReturnStatement yieldReturnStatement)
        {
            VisitStatement(yieldReturnStatement);
            Visit(yieldReturnStatement.Expression);
        }

        public virtual void VisitYieldBreakStatement(YieldBreakStatement yieldBreakStatement)
        {
            VisitStatement(yieldBreakStatement);
        }

        public virtual void VisitXmlComment(XmlComment xmlComment)
        {
        }

        public virtual void VisitWhileStatement(WhileStatement whileStatement)
        {
            VisitStatement(whileStatement);
            Visit(whileStatement.Condition);
            Visit(whileStatement.Body);
        }

        public virtual void VisitVariableReference(VariableReferenceExpression variableReferenceExpression)
        {
            VisitExpression(variableReferenceExpression);
        }

        public virtual void VisitVariableDeclarationStatement(VariableDeclarationStatement variableDeclarationStatement)
        {
            VisitStatement(variableDeclarationStatement);
            Visit(variableDeclarationStatement.InitExpression);
        }

        public virtual void VisitValueTypeTypeParameterConstraint(ValueTypeTypeParameterConstraint valueTypeTypeParameterConstraint)
        {
        }

        public virtual void VisitValueArgumentExpression(ValueArgumentExpression valueArgumentExpression)
        {
            VisitExpression(valueArgumentExpression);
        }

        public virtual void VisitUsingStatement(UsingStatement usingStatement)
        {
            VisitStatement(usingStatement);
            Visit(usingStatement.Statement);
            Visit(usingStatement.Body);
        }

        public virtual void VisitUsingDirective(UsingDirective usingDirective)
        {
            VisitDirective(usingDirective);
        }

        public virtual void VisitUnmanagedTypeParameterConstraint(UnmanagedTypeParameterConstraint unmanagedTypeParameterConstraint)
        {
        }

        public virtual void VisitUnaryExpression(UnaryExpression unaryExpression)
        {
            VisitExpression(unaryExpression);
            Visit(unaryExpression.Expression);
        }

        private void VisitTypeReferenceIfNotNull(TypeReference? typeReference)
        {
            if (typeReference != null)
            {
                VisitTypeReference(typeReference);
            }
        }

        public virtual void VisitTypeReference(TypeReference typeReference)
        {
        }

        public virtual void VisitTypeReferenceExpression(TypeReferenceExpression typeReference)
        {
            VisitTypeReferenceIfNotNull(typeReference.Type);
        }

        public virtual void VisitTypeOfExpression(TypeOfExpression typeOfExpression)
        {
            VisitExpression(typeOfExpression);
            VisitTypeReferenceIfNotNull(typeOfExpression.Type);
        }

        public virtual void VisitTryCatchFinallyStatement(TryCatchFinallyStatement tryCatchFinallyStatement)
        {
            VisitStatement(tryCatchFinallyStatement);
            Visit(tryCatchFinallyStatement.Try);
            Visit(tryCatchFinallyStatement.Catch);
            Visit(tryCatchFinallyStatement.Finally);
        }

        public virtual void VisitCatchClauseCollection(CatchClauseCollection catchClauseCollection)
        {
            VisitCollection(catchClauseCollection);
        }

        public virtual void VisitThrowStatement(ThrowStatement throwStatement)
        {
            VisitStatement(throwStatement);
            Visit(throwStatement.Expression);
        }

        public virtual void VisitThisExpression(ThisExpression thisExpression)
        {
            VisitExpression(thisExpression);
        }

        public virtual void VisitSnippetStatement(SnippetStatement snippetStatement)
        {
            VisitStatement(snippetStatement);
        }

        public virtual void VisitSnippetExpression(SnippetExpression snippetExpression)
        {
            VisitExpression(snippetExpression);
        }

        public virtual void VisitReturnStatement(ReturnStatement returnStatement)
        {
            VisitStatement(returnStatement);
            Visit(returnStatement.Expression);
        }

        public virtual void VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration)
        {
            VisitMemberDeclaration(propertyDeclaration);
            VisitTypeReferenceIfNotNull(propertyDeclaration.PrivateImplementationType);
            Visit(propertyDeclaration.Getter);
            Visit(propertyDeclaration.Setter);
        }

        public virtual void VisitPropertyAccessorDeclaration(PropertyAccessorDeclaration propertyAccessorDeclaration)
        {
            Visit(propertyAccessorDeclaration.Statements);
        }

        public virtual void VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration)
        {
            VisitMemberDeclaration(operatorDeclaration);
            VisitTypeReferenceIfNotNull(operatorDeclaration.ReturnType);
            VisitCollection(operatorDeclaration.Arguments);
            Visit(operatorDeclaration.Statements);
        }

        public virtual void VisitNewObjectExpression(NewObjectExpression newObjectExpression)
        {
            VisitExpression(newObjectExpression);
            Visit(newObjectExpression.Arguments);
            VisitTypeReferenceIfNotNull(newObjectExpression.Type);
        }

        public virtual void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
        {
            VisitCommentable(namespaceDeclaration);
            VisitCollection(namespaceDeclaration.Namespaces);
            VisitCollection(namespaceDeclaration.Types);
            VisitCollection(namespaceDeclaration.Usings);
        }

        public virtual void VisitNameofExpression(NameofExpression nameofExpression)
        {
            VisitExpression(nameofExpression);
            Visit(nameofExpression.Expression);
        }

        public virtual void VisitMethodInvokeExpression(MethodInvokeExpression methodInvokeExpression)
        {
            VisitExpression(methodInvokeExpression);
            VisitTypeReferenceCollection(methodInvokeExpression.Parameters);
            Visit(methodInvokeExpression.Arguments);
            Visit(methodInvokeExpression.Method);
        }

        public virtual void VisitMethodInvokeArgumentExpression(MethodInvokeArgumentExpression methodInvokeArgumentExpression)
        {
            VisitExpression(methodInvokeArgumentExpression);
            Visit(methodInvokeArgumentExpression.Value);
        }

        public virtual void VisitMethodExitStatement(MethodExitStatement methodExitStatement)
        {
            VisitStatement(methodExitStatement);
        }

        public virtual void VisitMethodDeclaration(MethodDeclaration methodDeclaration)
        {
            VisitMemberDeclaration(methodDeclaration);
            VisitTypeReferenceIfNotNull(methodDeclaration.ReturnType);
            VisitTypeReferenceIfNotNull(methodDeclaration.PrivateImplementationType);
            VisitCollection(methodDeclaration.Parameters);
            VisitCollection(methodDeclaration.Arguments);
            Visit(methodDeclaration.Statements);
        }

        public virtual void VisitMethodArgumentDeclaration(MethodArgumentDeclaration methodArgumentDeclaration)
        {
            VisitCommentable(methodArgumentDeclaration);
            VisitCustomAttributeContainer(methodArgumentDeclaration);
            VisitTypeReferenceIfNotNull(methodArgumentDeclaration.Type);
            Visit(methodArgumentDeclaration.DefaultValue);
        }

        public virtual void VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression)
        {
            VisitExpression(memberReferenceExpression);
        }

        public virtual void VisitLiteralExpression(LiteralExpression literalExpression)
        {
            VisitExpression(literalExpression);
        }

        public virtual void VisitIterationStatement(IterationStatement iterationStatement)
        {
            VisitStatement(iterationStatement);
            Visit(iterationStatement.Initialization);
            Visit(iterationStatement.Condition);
            Visit(iterationStatement.IncrementStatement);
            Visit(iterationStatement.Body);
        }

        public virtual void VisitGotoNextLoopIterationStatement(GotoNextLoopIterationStatement gotoNextLoopIterationStatement)
        {
            VisitStatement(gotoNextLoopIterationStatement);
        }

        public virtual void VisitFieldDeclaration(FieldDeclaration fieldDeclaration)
        {
            VisitMemberDeclaration(fieldDeclaration);
            VisitTypeReferenceIfNotNull(fieldDeclaration.Type);
            Visit(fieldDeclaration.InitExpression);
        }

        public virtual void VisitExpressionStatement(ExpressionStatement expressionStatement)
        {
            VisitStatement(expressionStatement);
            Visit(expressionStatement.Expression);
        }

        public virtual void VisitExitLoopStatement(ExitLoopStatement exitLoopStatement)
        {
            VisitStatement(exitLoopStatement);
        }

        public virtual void VisitEventFieldDeclaration(EventFieldDeclaration eventFieldDeclaration)
        {
            VisitMemberDeclaration(eventFieldDeclaration);
            VisitTypeReferenceIfNotNull(eventFieldDeclaration.Type);
            Visit(eventFieldDeclaration.AddAccessor);
            Visit(eventFieldDeclaration.RemoveAccessor);
            VisitTypeReferenceIfNotNull(eventFieldDeclaration.PrivateImplementationType);
        }

        public virtual void VisitEnumerationMember(EnumerationMember enumerationMember)
        {
            VisitMemberDeclaration(enumerationMember);
            VisitCollection(enumerationMember.Implements);
            Visit(enumerationMember.Value);
        }

        public virtual void VisitEnumerationDeclaration(EnumerationDeclaration enumerationDeclaration)
        {
            VisitTypeDeclaration(enumerationDeclaration);
            VisitTypeReferenceIfNotNull(enumerationDeclaration.BaseType);
            VisitCollection(enumerationDeclaration.Members);
        }

        public virtual void VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration)
        {
            VisitTypeDeclaration(delegateDeclaration);
            VisitTypeReferenceIfNotNull(delegateDeclaration.ReturnType);
            VisitCollection(delegateDeclaration.Parameters);
            VisitCollection(delegateDeclaration.Arguments);
        }

        public virtual void VisitDefaultValueExpression(DefaultValueExpression defaultValueExpression)
        {
            VisitExpression(defaultValueExpression);
        }

        public virtual void VisitCustomAttributeArgument(CustomAttributeArgument customAttributeArgument)
        {
            VisitCommentable(customAttributeArgument);
            Visit(customAttributeArgument.Value);
        }

        public virtual void VisitCustomAttribute(CustomAttribute customAttribute)
        {
            VisitCommentable(customAttribute);
            VisitCollection(customAttribute.Arguments);
            VisitTypeReferenceIfNotNull(customAttribute.Type);
        }

        public virtual void VisitConvertExpression(ConvertExpression convertExpression)
        {
            VisitExpression(convertExpression);
            Visit(convertExpression.Expression);
            VisitTypeReferenceIfNotNull(convertExpression.Type);
        }

        public virtual void VisitConstructorThisInitializer(ConstructorThisInitializer constructorThisInitializer)
        {
            VisitConstructorInitializer(constructorThisInitializer);
        }

        public virtual void VisitConstructorParameterConstraint(ConstructorParameterConstraint constructorParameterConstraint)
        {
        }

        public virtual void VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration)
        {
            VisitMemberDeclaration(constructorDeclaration);
            VisitCollection(constructorDeclaration.Arguments);
            Visit(constructorDeclaration.Statements);
            Visit(constructorDeclaration.Initializer);
        }

        public virtual void VisitConstructorBaseInitializer(ConstructorBaseInitializer constructorBaseInitializer)
        {
            VisitConstructorInitializer(constructorBaseInitializer);
        }

        public virtual void VisitConditionStatement(ConditionStatement conditionStatement)
        {
            VisitStatement(conditionStatement);
            Visit(conditionStatement.Condition);
            Visit(conditionStatement.TrueStatements);
            Visit(conditionStatement.FalseStatements);
        }

        public virtual void VisitCommentStatement(CommentStatement commentStatement)
        {
            VisitStatement(commentStatement);
        }

        public virtual void VisitComment(Comment comment)
        {
        }

        public virtual void VisitClassTypeParameterConstraint(ClassTypeParameterConstraint classTypeParameterConstraint)
        {
        }

        public virtual void VisitCatchClause(CatchClause catchClause)
        {
            VisitCommentable(catchClause);
            VisitTypeReferenceIfNotNull(catchClause.ExceptionType);
            Visit(catchClause.Body);
        }

        public virtual void VisitCastExpression(CastExpression castExpression)
        {
            VisitExpression(castExpression);
            Visit(castExpression.Expression);
            VisitTypeReferenceIfNotNull(castExpression.Type);
        }

        public virtual void VisitBinaryExpression(BinaryExpression binaryExpression)
        {
            VisitExpression(binaryExpression);
            Visit(binaryExpression.LeftExpression);
            Visit(binaryExpression.RightExpression);
        }

        public virtual void VisitBaseTypeParameterConstraint(BaseTypeParameterConstraint baseTypeParameterConstraint)
        {
        }

        public virtual void VisitBaseExpression(BaseExpression baseExpression)
        {
            VisitExpression(baseExpression);
        }

        public virtual void VisitAwaitExpression(AwaitExpression awaitExpression)
        {
            VisitExpression(awaitExpression);
            Visit(awaitExpression.Expression);
        }

        public virtual void VisitAssignStatement(AssignStatement assignStatement)
        {
            VisitStatement(assignStatement);
            Visit(assignStatement.LeftExpression);
            Visit(assignStatement.RightExpression);
        }

        public virtual void VisitArgumentReferenceExpression(ArgumentReferenceExpression argumentReferenceExpression)
        {
            VisitExpression(argumentReferenceExpression);
        }

        public virtual void VisitAddEventHandlerStatement(AddEventHandlerStatement addEventHandlerStatement)
        {
            VisitStatement(addEventHandlerStatement);
            Visit(addEventHandlerStatement.LeftExpression);
            Visit(addEventHandlerStatement.RightExpression);
        }

        public virtual void VisitRemoveEventHandlerStatement(RemoveEventHandlerStatement removeEventHandlerStatement)
        {
            VisitStatement(removeEventHandlerStatement);
            Visit(removeEventHandlerStatement.LeftExpression);
            Visit(removeEventHandlerStatement.RightExpression);
        }

        public virtual void VisitArrayIndexerExpression(ArrayIndexerExpression arrayIndexerExpression)
        {
            VisitExpression(arrayIndexerExpression);
            Visit(arrayIndexerExpression.ArrayExpression);
            Visit(arrayIndexerExpression.Indices);
        }

        public virtual void VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
        {
            VisitTypeDeclaration(interfaceDeclaration);
            VisitMemberContainer(interfaceDeclaration);
            VisitParametrableType(interfaceDeclaration);
            VisitTypeDeclarationContainer(interfaceDeclaration);
            VisitTypeReferenceCollection(interfaceDeclaration.Implements);
            VisitTypeReferenceIfNotNull(interfaceDeclaration.BaseType);
        }

        public virtual void VisitTypeParameterConstraint(TypeParameterConstraint typeParameterConstraint)
        {
        }

        public virtual void VisitTypeParameter(TypeParameter typeParameter)
        {
            VisitCollection(typeParameter.Constraints);
        }

        public virtual void VisitCompilationUnit(CompilationUnit compilationUnit)
        {
            VisitCollection(compilationUnit.Namespaces);
            VisitCollection(compilationUnit.Types);
            VisitCollection(compilationUnit.Usings);
        }

        public virtual void VisitNamespace(NamespaceDeclaration namespaceDeclaration)
        {
            VisitCollection(namespaceDeclaration.Namespaces);
            VisitCollection(namespaceDeclaration.Types);
            VisitCollection(namespaceDeclaration.Usings);
        }

        public virtual void VisitStructDeclaration(StructDeclaration structDeclaration)
        {
            VisitTypeDeclaration(structDeclaration);
            VisitMemberContainer(structDeclaration);
            VisitParametrableType(structDeclaration);
            VisitTypeDeclarationContainer(structDeclaration);
            VisitTypeReferenceCollection(structDeclaration.Implements);
        }

        public virtual void VisitClassDeclaration(ClassDeclaration classDeclaration)
        {
            VisitTypeDeclaration(classDeclaration);
            VisitMemberContainer(classDeclaration);
            VisitParametrableType(classDeclaration);
            VisitTypeDeclarationContainer(classDeclaration);
            VisitTypeReferenceCollection(classDeclaration.Implements);
            VisitTypeReferenceIfNotNull(classDeclaration.BaseType);
        }

        public virtual void VisitExpressions(CodeObjectCollection<Expression> expressions)
        {
            VisitCollection(expressions);
        }

        public virtual void VisitStatementCollection(StatementCollection statements)
        {
            VisitCollection(statements);
        }

        private void VisitCollection<T>(CodeObjectCollection<T> items) where T : CodeObject
        {
            if (items == null)
                return;

            foreach (var item in items)
            {
                Visit(item);
            }
        }

        private void VisitTypeReferenceCollection(IEnumerable<TypeReference> items)
        {
            if (items == null)
                return;

            foreach (var item in items)
            {
                VisitTypeReferenceIfNotNull(item);
            }
        }

        private void VisitCommentable(ICommentable commentable)
        {
            Visit(commentable.CommentsBefore);
            Visit(commentable.CommentsAfter);
        }

        private void VisitXmlCommentable(IXmlCommentable commentable)
        {
            Visit(commentable.XmlComments);
        }

        private void VisitCustomAttributeContainer(ICustomAttributeContainer commentable)
        {
            VisitCollection(commentable.CustomAttributes);
        }

        private void VisitParametrableType(IParametrableType parametrableType)
        {
            VisitCollection(parametrableType.Parameters);
        }

        private void VisitTypeDeclarationContainer(ITypeDeclarationContainer typeDeclarationContainer)
        {
            VisitCollection(typeDeclarationContainer.Types);
        }

        private void VisitMemberContainer(IMemberContainer memberContainer)
        {
            VisitCollection(memberContainer.Members);
        }

        private void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
        {
            VisitCustomAttributeContainer(typeDeclaration);
            VisitCommentable(typeDeclaration);
            VisitXmlCommentable(typeDeclaration);
        }

        private void VisitMemberDeclaration(MemberDeclaration memberDeclaration)
        {
            VisitCustomAttributeContainer(memberDeclaration);
            VisitCommentable(memberDeclaration);
            VisitXmlCommentable(memberDeclaration);
            VisitCollection(memberDeclaration.Implements);
        }

        private void VisitExpression(Expression expression)
        {
            VisitCommentable(expression);
        }

        private void VisitStatement(Statement statement)
        {
            VisitCommentable(statement);
        }

        private void VisitDirective(Directive directive)
        {
            VisitCommentable(directive);
        }

        private void VisitConstructorInitializer(ConstructorInitializer constructor)
        {
            VisitCommentable(constructor);
            Visit(constructor.Arguments);
        }
    }
}
